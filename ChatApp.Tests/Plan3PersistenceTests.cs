using ChatApp.Core.Models;
using ChatApp.Infrastructure;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

/// <summary>
/// Coverage for plan-3.5: query indexes (fresh + legacy upgrade path, idempotent)
/// and the beforeId cursor used by the "load earlier messages" window.
/// </summary>
public class Plan3PersistenceTests
{
    [Fact]
    public async Task Plan3IndexesExistOnFreshDatabases()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                await InfrastructureModule.MigratePlan3IndexesAsync(db, CancellationToken.None);
            }

            await using var conn = new SqliteConnection($"Data Source={path}");
            await conn.OpenAsync();
            Assert.True(await IndexExistsAsync(conn, "IX_Messages_ConversationId_Id"));
            Assert.True(await IndexExistsAsync(conn, "IX_Conversations_IsPinned_UpdatedAt"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Plan3IndexMigrationIsIdempotentOnOldDatabases()
    {
        var path = NewDatabasePath();
        try
        {
            // Simulate a pre-2.x database (no IsPinned, no CitedDocumentIds).
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var createMessages = new SqliteCommand(
                    "CREATE TABLE \"Messages\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                    "\"ConversationId\" INTEGER NOT NULL, \"RoleId\" INTEGER NOT NULL, " +
                    "\"Author\" INTEGER NOT NULL, \"Content\" TEXT NOT NULL, " +
                    "\"TokenEstimate\" INTEGER NOT NULL, \"CreatedAt\" TEXT NOT NULL);", connection);
                await createMessages.ExecuteNonQueryAsync();
                await using var createConversations = new SqliteCommand(
                    "CREATE TABLE \"Conversations\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                    "\"RoleId\" INTEGER NULL, \"Type\" INTEGER NOT NULL DEFAULT 0, " +
                    "\"Title\" TEXT NOT NULL DEFAULT '', " +
                    "\"CreatedAt\" TEXT NOT NULL, \"UpdatedAt\" TEXT NOT NULL);", connection);
                await createConversations.ExecuteNonQueryAsync();
            }

            var factory = new TestDbContextFactory(path);
            for (var i = 0; i < 2; i++)
            {
                await using var db = await factory.CreateDbContextAsync();
                await InfrastructureModule.MigrateConversationExtrasAsync(db, CancellationToken.None);
                await InfrastructureModule.MigratePlan3IndexesAsync(db, CancellationToken.None);
            }

            await using var verify = new SqliteConnection($"Data Source={path}");
            await verify.OpenAsync();
            Assert.True(await IndexExistsAsync(verify, "IX_Messages_ConversationId_Id"));
            Assert.True(await IndexExistsAsync(verify, "IX_Conversations_IsPinned_UpdatedAt"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task BeforeIdCursorWalksOlderPagesInAscendingOrder()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                await InfrastructureModule.MigrateConversationExtrasAsync(db, CancellationToken.None);
            }

            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
            var conv = await history.CreateConversationAsync(1, "对话");
            var ids = new List<int>();
            for (var i = 1; i <= 5; i++)
            {
                var msg = await history.AddMessageAsync(new Message
                {
                    ConversationId = conv.Id,
                    RoleId = 1,
                    Author = i % 2 == 0 ? MessageAuthor.Assistant : MessageAuthor.User,
                    Content = $"m{i}"
                });
                ids.Add(msg.Id);
            }

            var newest = await history.GetMessagesAsync(conv.Id, limit: 2, beforeId: null);
            Assert.Equal(new[] { ids[3], ids[4] }, newest.Select(m => m.Id));

            var middle = await history.GetMessagesAsync(conv.Id, limit: 2, beforeId: newest[0].Id);
            Assert.Equal(new[] { ids[1], ids[2] }, middle.Select(m => m.Id));

            var oldest = await history.GetMessagesAsync(conv.Id, limit: 2, beforeId: middle[0].Id);
            Assert.Equal(new[] { ids[0] }, oldest.Select(m => m.Id));

            Assert.Empty(await history.GetMessagesAsync(conv.Id, limit: 2, beforeId: oldest[0].Id));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task<bool> IndexExistsAsync(SqliteConnection conn, string name)
    {
        await using var cmd = new SqliteCommand(
            "SELECT count(*) FROM sqlite_master WHERE type='index' AND name=$name;", conn);
        cmd.Parameters.AddWithValue("$name", name);
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"chatapp-tests-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(string path)
        {
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}