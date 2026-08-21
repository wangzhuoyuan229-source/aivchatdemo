using ChatApp.Core.Models;
using ChatApp.Core.Settings;
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
    public async Task SharedMemoryMigrationMergesLegacyVectorScopesAndIsIdempotent()
    {
        var path = NewDatabasePath();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var createVectors = new SqliteCommand(
                    "CREATE TABLE \"Vectors\" (\"Id\" TEXT PRIMARY KEY, \"Scope\" TEXT NOT NULL, " +
                    "\"Content\" TEXT NOT NULL, \"Embedding\" BLOB NOT NULL, \"Metadata\" TEXT NOT NULL DEFAULT '{}');",
                    connection);
                await createVectors.ExecuteNonQueryAsync();
                await using var insertVectors = new SqliteCommand(
                    "INSERT INTO \"Vectors\" (\"Id\", \"Scope\", \"Content\", \"Embedding\") VALUES " +
                    "('m1', 'memory:1', '一', X'00000000'), " +
                    "('m2', 'memory:2', '二', X'00000000'), " +
                    "('k1', 'knowledge:1', '资料', X'00000000');",
                    connection);
                await insertVectors.ExecuteNonQueryAsync();
            }

            var factory = new TestDbContextFactory(path);
            for (var i = 0; i < 2; i++)
            {
                await using var db = await factory.CreateDbContextAsync();
                await InfrastructureModule.MigrateSharedMemoryAsync(db, CancellationToken.None);
            }

            await using var verify = new SqliteConnection($"Data Source={path}");
            await verify.OpenAsync();
            await using var command = new SqliteCommand(
                "SELECT \"Id\", \"Scope\" FROM \"Vectors\" ORDER BY \"Id\";", verify);
            await using var reader = await command.ExecuteReaderAsync();
            var scopes = new Dictionary<string, string>();
            while (await reader.ReadAsync()) scopes[reader.GetString(0)] = reader.GetString(1);

            Assert.Equal("memory:shared", scopes["m1"]);
            Assert.Equal("memory:shared", scopes["m2"]);
            Assert.Equal("knowledge:1", scopes["k1"]);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task RolePromptTemplateMigrationPreservesLegacyRolesAndIsIdempotent()
    {
        var path = NewDatabasePath();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var createRoles = new SqliteCommand(
                    "CREATE TABLE \"Roles\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                    "\"Name\" TEXT NOT NULL, \"CreatedAt\" TEXT NOT NULL);", connection);
                await createRoles.ExecuteNonQueryAsync();
                await using var insertRole = new SqliteCommand(
                    "INSERT INTO \"Roles\" (\"Name\", \"CreatedAt\") VALUES ('旧角色', CURRENT_TIMESTAMP);", connection);
                await insertRole.ExecuteNonQueryAsync();
            }

            var factory = new TestDbContextFactory(path);
            for (var i = 0; i < 2; i++)
            {
                await using var db = await factory.CreateDbContextAsync();
                await InfrastructureModule.MigrateRolePromptTemplateAsync(db, CancellationToken.None);
            }

            await using var verify = new SqliteConnection($"Data Source={path}");
            await verify.OpenAsync();
            await using var command = new SqliteCommand(
                "SELECT \"Name\", \"PromptTemplateVersion\" FROM \"Roles\" WHERE \"Id\" = 1;", verify);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("旧角色", reader.GetString(0));
            Assert.Equal(0, reader.GetInt32(1));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task FreshDatabasePersistsCurrentRolePromptTemplateVersion()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Roles.Add(new Role
                {
                    Name = "新角色",
                    PromptTemplateVersion = Role.CurrentPromptTemplateVersion
                });
                await db.SaveChangesAsync();
            }

            await using var verify = await factory.CreateDbContextAsync();
            Assert.Equal(
                Role.CurrentPromptTemplateVersion,
                await verify.Roles.Select(role => role.PromptTemplateVersion).SingleAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CorruptSettingsJsonFallsBackToUsableCurrentDefaults()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Settings.Add(new Setting { Key = "ai", Value = "{not-json" });
                await db.SaveChangesAsync();
            }

            var settings = await new ConfigurationService(factory).LoadAsync();

            Assert.True(settings.UseUnifiedApi);
            Assert.Equal(AiSettings.DefaultApiBaseUrl, settings.ApiBaseUrl);
            Assert.Equal(AiSettings.DefaultChatModel, settings.ChatModel);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

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
