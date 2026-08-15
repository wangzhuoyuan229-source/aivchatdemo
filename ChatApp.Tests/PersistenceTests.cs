using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task GroundingMigrationIsIdempotentForAnOldRolesTable()
    {
        var path = NewDatabasePath();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Roles (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);";
                await command.ExecuteNonQueryAsync();
            }

            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await InfrastructureModule.MigrateGroundedDialogueAsync(db, CancellationToken.None);
                await InfrastructureModule.MigrateGroundedDialogueAsync(db, CancellationToken.None);
            }

            await using var verify = new SqliteConnection($"Data Source={path}");
            await verify.OpenAsync();
            Assert.True(await ColumnExistsAsync(verify, "Roles", "DialogueExamples"));
            Assert.True(await TableExistsAsync(verify, "RoleKnowledgeGroups"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task RoleBindingsCanBeReplacedAndAreRemovedWithRole()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            int roleId;
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                var role = new Role { Name = "角色" };
                db.Roles.Add(role);
                db.KnowledgeGroups.AddRange(
                    new KnowledgeGroup { Name = "组一" },
                    new KnowledgeGroup { Name = "组二" });
                await db.SaveChangesAsync();
                roleId = role.Id;
            }

            var service = new RoleService(factory, NullLogger<RoleService>.Instance);
            await service.SetKnowledgeGroupIdsAsync(roleId, new[] { 1, 2, 2 });
            Assert.Equal(new[] { 1, 2 }, await service.GetKnowledgeGroupIdsAsync(roleId));

            await service.SetKnowledgeGroupIdsAsync(roleId, new[] { 2 });
            Assert.Equal(new[] { 2 }, await service.GetKnowledgeGroupIdsAsync(roleId));

            await service.DeleteAsync(roleId);
            Assert.Empty(await service.GetKnowledgeGroupIdsAsync(roleId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DeletingKnowledgeGroupAlsoRemovesRoleBindings()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            int roleId;
            int groupId;
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                var role = new Role { Name = "角色" };
                var group = new KnowledgeGroup { Name = "设定" };
                db.AddRange(role, group);
                await db.SaveChangesAsync();
                roleId = role.Id;
                groupId = group.Id;
                db.RoleKnowledgeGroups.Add(new RoleKnowledgeGroup
                {
                    RoleId = roleId,
                    KnowledgeGroupId = groupId
                });
                await db.SaveChangesAsync();
            }

            var knowledgeService = new KnowledgeService(
                factory,
                new UnusedEmbeddingService(),
                new UnusedVectorStore(),
                NullLogger<KnowledgeService>.Instance);
            await knowledgeService.DeleteGroupAsync(groupId, deleteDocuments: false);

            var roleService = new RoleService(factory, NullLogger<RoleService>.Instance);
            Assert.Empty(await roleService.GetKnowledgeGroupIdsAsync(roleId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ChatHistoryWindowReturnsNewestMessagesInChronologicalOrder()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            int conversationId;
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                var conversation = new Conversation { RoleId = 1, Type = ConversationType.Private };
                db.Conversations.Add(conversation);
                await db.SaveChangesAsync();
                conversationId = conversation.Id;
                for (var i = 1; i <= 4; i++)
                {
                    db.Messages.Add(new Message
                    {
                        ConversationId = conversationId,
                        RoleId = 1,
                        Author = i % 2 == 0 ? MessageAuthor.Assistant : MessageAuthor.User,
                        Content = $"消息{i}"
                    });
                }
                await db.SaveChangesAsync();
            }

            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
            var window = await history.GetMessagesAsync(conversationId, limit: 2);

            Assert.Equal(new[] { "消息3", "消息4" }, window.Select(m => m.Content));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name;";
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

    private sealed class UnusedEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected in this test.");

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected in this test.");
    }

    private sealed class UnusedVectorStore : IVectorStore
    {
        public Task UpsertAsync(VectorRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string scope, int topK, double minScore = 0, IReadOnlySet<string>? allowedIds = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteByScopeAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
    }
}
