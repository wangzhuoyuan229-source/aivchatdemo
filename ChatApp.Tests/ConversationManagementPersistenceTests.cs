using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Repositories;
using ChatApp.Infrastructure.VectorStore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

/// <summary>
/// Persistence coverage for roadmap 2.x: conversation pinning, rename,
/// message citation column and DeleteMessagesFromAsync truncation.
/// </summary>
public class ConversationManagementPersistenceTests
{
    [Fact]
    public async Task PinnedConversationsSortBeforeUnpinnedOnes()
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
            var older = await history.CreateConversationAsync(1, "older");
            var newer = await history.CreateConversationAsync(1, "newer");
            await Task.Delay(20);
            await history.SetConversationPinnedAsync(older.Id, true);

            var list = await history.GetConversationsAsync();

            Assert.Equal(older.Id, list[0].Id);
            Assert.Equal(newer.Id, list[1].Id);
            Assert.True(list[0].IsPinned);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task RenamePersistsAndIsIdempotentForBlankTitles()
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
            var conv = await history.CreateConversationAsync(1, "旧名称");

            await history.RenameConversationAsync(conv.Id, "  新名称  ");
            await history.RenameConversationAsync(conv.Id, "   ");

            var reloaded = await history.GetConversationAsync(conv.Id);
            Assert.Equal("新名称", reloaded!.Title);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task GroupAvatarPersistsAndEmptyAvatarRemainsAvailableForCollageFallback()
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
            var custom = await history.CreateGroupConversationAsync("自定义头像", [1, 2], "data:image/png;base64,AA==");
            var fallback = await history.CreateGroupConversationAsync("成员拼图", [1, 2]);

            Assert.Equal("data:image/png;base64,AA==", (await history.GetConversationAsync(custom.Id))!.Avatar);
            Assert.Equal(string.Empty, (await history.GetConversationAsync(fallback.Id))!.Avatar);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DeletingGroupConversationRemovesMembersMessagesAttachmentsAndDerivedMemories()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Roles.AddRange(new Role { Name = "角色一" }, new Role { Name = "角色二" });
                await db.SaveChangesAsync();
            }

            var vectors = new RecordingDeleteVectorStore();
            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance, vectors);
            var group = await history.CreateGroupConversationAsync("待删除群聊", [1, 2]);
            await history.AddMessageAsync(new Message
            {
                ConversationId = group.Id,
                RoleId = 1,
                Author = MessageAuthor.Assistant,
                Content = "群聊消息",
                Attachments = [new MessageAttachment { FileName = "快照.png" }]
            });
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.MemoryEntries.Add(new MemoryEntry
                {
                    RoleId = 1,
                    ConversationId = group.Id,
                    Content = "群聊记忆",
                    ExternalId = "mem:group-delete"
                });
                await db.SaveChangesAsync();
            }

            await history.DeleteConversationAsync(group.Id);
            await history.DeleteConversationAsync(group.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => history.AddMessageAsync(new Message
            {
                ConversationId = group.Id,
                RoleId = 1,
                Author = MessageAuthor.Assistant,
                Content = "迟到回复"
            }));

            await using var verify = await factory.CreateDbContextAsync();
            Assert.Null(await verify.Conversations.FindAsync(group.Id));
            Assert.Empty(await verify.ConversationMembers.Where(member => member.ConversationId == group.Id).ToListAsync());
            Assert.Empty(await verify.Messages.Where(message => message.ConversationId == group.Id).ToListAsync());
            Assert.Empty(await verify.MessageAttachments.ToListAsync());
            Assert.Empty(await verify.MemoryEntries.Where(memory => memory.ConversationId == group.Id).ToListAsync());
            Assert.Equal(2, await verify.Roles.CountAsync());
            Assert.Equal(new[] { "mem:group-delete" }, vectors.DeletedIds);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DeleteMessagesFromAsyncRemovesTailAndKeepsEarlierMessages()
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
            var m1 = await history.AddMessageAsync(new Message { ConversationId = conv.Id, RoleId = 1, Author = MessageAuthor.User, Content = "1" });
            var m2 = await history.AddMessageAsync(new Message { ConversationId = conv.Id, RoleId = 1, Author = MessageAuthor.Assistant, Content = "2" });
            var m3 = await history.AddMessageAsync(new Message { ConversationId = conv.Id, RoleId = 1, Author = MessageAuthor.User, Content = "3" });

            var removed = await history.DeleteMessagesFromAsync(conv.Id, m2.Id);

            Assert.Equal(2, removed);
            var remaining = await history.GetMessagesAsync(conv.Id);
            Assert.Single(remaining);
            Assert.Equal(m1.Id, remaining[0].Id);
            // Idempotent: nothing left to remove from m2's id onward.
            Assert.Equal(0, await history.DeleteMessagesFromAsync(conv.Id, m2.Id));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CitationColumnRoundTripsThroughTheDatabase()
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
            var saved = await history.AddMessageAsync(new Message
            {
                ConversationId = conv.Id,
                RoleId = 1,
                Author = MessageAuthor.Assistant,
                Content = "grounded reply",
                CitedDocumentIds = "3,7"
            });

            var reloaded = await history.GetMessagesAsync(conv.Id);

            Assert.Equal("3,7", reloaded[0].CitedDocumentIds);
            Assert.Equal(new[] { 3, 7 }, reloaded[0].GetCitedDocumentIdList());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CitationMigrationIsIdempotentOnOldDatabases()
    {
        var path = NewDatabasePath();
        try
        {
            // Simulate a pre-2.x database without IsPinned / CitedDocumentIds.
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
            }

            await using var verify = new SqliteConnection($"Data Source={path}");
            await verify.OpenAsync();
            await using var checkConv = new SqliteCommand(
                "SELECT count(*) FROM pragma_table_info('Conversations') WHERE name='IsPinned';", verify);
            await using var checkAvatar = new SqliteCommand(
                "SELECT count(*) FROM pragma_table_info('Conversations') WHERE name='Avatar';", verify);
            await using var checkMsg = new SqliteCommand(
                "SELECT count(*) FROM pragma_table_info('Messages') WHERE name='CitedDocumentIds';", verify);
            Assert.Equal(1L, (long)(await checkConv.ExecuteScalarAsync())!);
            Assert.Equal(1L, (long)(await checkAvatar.ExecuteScalarAsync())!);
            Assert.Equal(1L, (long)(await checkMsg.ExecuteScalarAsync())!);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task MemoryUpdateRewritesContentAndReEmbedsVector()
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

            var embedding = new RecordingEmbeddingService();
            var vectors = new SqliteVectorStore(NullLogger<SqliteVectorStore>.Instance,
                $"Data Source={path};Pooling=False");
            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
            var config = new FixedConfigurationService(new AiSettings());
            var memory = new MemoryService(factory, embedding, vectors, history, config,
                NullLogger<MemoryService>.Instance);

            var entry = new MemoryEntry { RoleId = 1, Content = "旧记忆", ExternalId = "mem:1:a" };
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.MemoryEntries.Add(entry);
                await db.SaveChangesAsync();
            }
            await vectors.UpsertAsync(new VectorRecord
            {
                Id = "mem:1:a",
                Scope = "memory:1",
                Content = "旧记忆",
                Embedding = new[] { 1f, 0f }
            });

            await memory.UpdateAsync(entry.Id, " 新记忆 ");

            var list = await memory.ListAllAsync();
            Assert.Single(list);
            Assert.Equal("新记忆", list[0].Content);
            Assert.Equal("mem:1:a", list[0].ExternalId);
            // The vector was re-embedded under the same key with the new content.
            Assert.Equal(1, embedding.CallCount);
            Assert.Contains("新记忆", embedding.EmbeddedTexts);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task SharedMemoryRecallIncludesMemoriesFromEverySourceRole()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Roles.AddRange(new Role { Name = "阿澄" }, new Role { Name = "林溪" });
                await db.SaveChangesAsync();
            }

            var embedding = new RecordingEmbeddingService();
            var vectors = new SqliteVectorStore(NullLogger<SqliteVectorStore>.Instance,
                $"Data Source={path};Pooling=False");
            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
            var memory = new MemoryService(
                factory,
                embedding,
                vectors,
                history,
                new FixedConfigurationService(new AiSettings { MemoryTopK = 5 }),
                NullLogger<MemoryService>.Instance);

            await memory.RememberAsync(1, null, "阿澄触发的事件");
            await memory.RememberAsync(2, null, "林溪触发的事件");

            var hits = await memory.RecallSharedAsync("共同经历");

            Assert.Equal(2, hits.Count);
            Assert.Equal(new[] { "阿澄", "林溪" },
                hits.Select(hit => hit.Record.Metadata["sourceRoleName"]).OrderBy(name => name));
            Assert.Equal(2, (await memory.ListAllAsync()).Count);

            await memory.ClearAllAsync();
            Assert.Empty(await memory.ListAllAsync());
            Assert.Empty(await memory.RecallSharedAsync("共同经历"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task GroupConversationExtractsSharedMemoriesWithEachSpeakerAsSource()
    {
        var path = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(path);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.Roles.AddRange(new Role { Name = "角色一" }, new Role { Name = "角色二" });
                await db.SaveChangesAsync();
            }

            var history = new ChatHistoryService(factory, NullLogger<ChatHistoryService>.Instance);
            var conversation = await history.CreateGroupConversationAsync("共享事件", [1, 2]);
            await history.AddMessageAsync(new Message
            {
                ConversationId = conversation.Id,
                RoleId = 0,
                Author = MessageAuthor.User,
                Content = "大家记住今天的约定"
            });
            await history.AddMessageAsync(new Message
            {
                ConversationId = conversation.Id,
                RoleId = 1,
                Author = MessageAuthor.Assistant,
                Content = "角色一答应了"
            });
            await history.AddMessageAsync(new Message
            {
                ConversationId = conversation.Id,
                RoleId = 2,
                Author = MessageAuthor.Assistant,
                Content = "角色二也答应了"
            });

            var memory = new MemoryService(
                factory,
                new RecordingEmbeddingService(),
                new SqliteVectorStore(NullLogger<SqliteVectorStore>.Instance, $"Data Source={path};Pooling=False"),
                history,
                new FixedConfigurationService(new AiSettings { MemoryBatchSize = 3 }),
                NullLogger<MemoryService>.Instance);

            await memory.ProcessConversationAsync(conversation.Id);

            var entries = await memory.ListAllAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(new[] { 1, 2 }, entries.Select(entry => entry.RoleId).OrderBy(id => id));
            Assert.All(entries, entry => Assert.Equal(conversation.Id, entry.ConversationId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        public int CallCount { get; private set; }
        public List<string> EmbeddedTexts { get; } = new();

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            CallCount++;
            EmbeddedTexts.Add(text);
            return Task.FromResult(new[] { 0.5f, 0.5f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            CallCount++;
            EmbeddedTexts.AddRange(texts);
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.5f, 0.5f }).ToArray());
        }
    }

    private sealed class FixedConfigurationService(AiSettings settings) : IConfigurationService
    {
        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(settings);
        public Task SaveAsync(AiSettings value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class RecordingDeleteVectorStore : IVectorStore
    {
        public List<string> DeletedIds { get; } = new();

        public Task UpsertAsync(VectorRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            float[] queryVector,
            string scope,
            int topK,
            double minScore = 0,
            IReadOnlySet<string>? allowedIds = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorSearchHit>>(Array.Empty<VectorSearchHit>());

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            DeletedIds.Add(id);
            return Task.CompletedTask;
        }

        public Task DeleteByScopeAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
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
