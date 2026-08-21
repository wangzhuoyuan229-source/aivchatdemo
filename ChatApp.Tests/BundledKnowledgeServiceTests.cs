using System.Collections.Concurrent;
using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure.Data;
using ChatApp.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public sealed class BundledKnowledgeServiceTests
{
    [Fact]
    public async Task FirstRunIndexesBundleAndSecondRunUsesPersistentMarker()
    {
        var root = NewTestRoot();
        try
        {
            var bundle = Path.Combine(root, "BundledKnowledge");
            var settings = Path.Combine(bundle, "设定", "人物");
            Directory.CreateDirectory(settings);
            await File.WriteAllTextAsync(Path.Combine(settings, "阿米娅.txt"), "阿米娅是罗德岛的公开领袖。");
            await File.WriteAllTextAsync(Path.Combine(settings, "无扩展名资料"), "这也是知识资料。");
            await File.WriteAllBytesAsync(Path.Combine(settings, ".DS_Store"), new byte[] { 0, 1, 2, 3 });

            var embedding = new CountingEmbeddingService();
            var knowledge = await CreateKnowledgeServiceAsync(Path.Combine(root, "knowledge.db"), embedding);
            var service = CreateBundledService(root, bundle, knowledge);

            var first = await service.EnsureImportedAsync();
            var embeddingCallsAfterFirstRun = embedding.InputCount;
            var second = await service.EnsureImportedAsync();

            Assert.Equal(BundledKnowledgeImportStatus.Imported, first.Status);
            Assert.Equal(2, first.Total);
            Assert.Equal(2, first.Imported);
            Assert.Equal(BundledKnowledgeImportStatus.AlreadyCurrent, second.Status);
            Assert.Equal(embeddingCallsAfterFirstRun, embedding.InputCount);
            var group = Assert.Single(await knowledge.ListGroupsAsync(), item => item.Name == BundledKnowledgeService.GroupName);
            var documents = await knowledge.ListDocumentsByGroupAsync(group.Id);
            Assert.Equal(2, documents.Count);
            Assert.Contains(documents, item => item.SourceRelativePath == "设定/人物/阿米娅.txt");
            Assert.Contains(documents, item => item.SourceRelativePath == "设定/人物/无扩展名资料");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingUngroupedImportIsAdoptedWithoutRegeneratingVectors()
    {
        var root = NewTestRoot();
        try
        {
            var bundle = Path.Combine(root, "BundledKnowledge");
            var settings = Path.Combine(bundle, "设定");
            Directory.CreateDirectory(settings);
            await File.WriteAllTextAsync(Path.Combine(settings, "世界观.txt"), "泰拉存在源石与天灾。");

            var embedding = new CountingEmbeddingService();
            var knowledge = await CreateKnowledgeServiceAsync(Path.Combine(root, "knowledge.db"), embedding);
            await knowledge.ImportDirectoryAsync(settings, recursive: true);
            var callsBeforeBundledImport = embedding.InputCount;
            var service = CreateBundledService(root, bundle, knowledge);

            var result = await service.EnsureImportedAsync();

            Assert.Equal(BundledKnowledgeImportStatus.Imported, result.Status);
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.MovedFromUngrouped);
            Assert.Equal(callsBeforeBundledImport, embedding.InputCount);
            Assert.Empty(await knowledge.ListDocumentsByGroupAsync(null));
            Assert.Single(await knowledge.ListDocumentsByGroupAsync(result.GroupId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BundledKnowledgeService CreateBundledService(
        string root,
        string bundle,
        IKnowledgeService knowledge) =>
        new(
            knowledge,
            new FixedConfigurationService(),
            NullLogger<BundledKnowledgeService>.Instance,
            bundle,
            Path.Combine(root, "bundled-knowledge.json"));

    private static string NewTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatapp-bundled-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<KnowledgeService> CreateKnowledgeServiceAsync(
        string databasePath,
        CountingEmbeddingService embedding)
    {
        var factory = new TestDbContextFactory(databasePath);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        return new KnowledgeService(
            factory,
            embedding,
            new FakeVectorStore(),
            NullLogger<KnowledgeService>.Instance);
    }

    private sealed class FixedConfigurationService : IConfigurationService
    {
        private readonly AiSettings _settings = new()
        {
            UseUnifiedApi = false,
            EnableKnowledgeBase = true,
            EmbeddingApiBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            EmbeddingApiKey = "test-only-key",
            EmbeddingModel = "test-embedding"
        };

        public Task<AiSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);

        public Task SaveAsync(AiSettings settings, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class CountingEmbeddingService : IEmbeddingService
    {
        public int InputCount;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Interlocked.Increment(ref InputCount);
            return Task.FromResult(new[] { 1f, 0f });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            Interlocked.Add(ref InputCount, texts.Count);
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToArray());
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(string path)
        {
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .Options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _records = new();

        public Task UpsertAsync(VectorRecord record, CancellationToken ct = default)
        {
            _records[record.Id] = record;
            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default)
        {
            foreach (var record in records) _records[record.Id] = record;
            return Task.CompletedTask;
        }

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
            _records.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task DeleteByScopeAsync(string scope, CancellationToken ct = default)
        {
            foreach (var item in _records.Where(item => item.Value.Scope == scope))
                _records.TryRemove(item.Key, out _);
            return Task.CompletedTask;
        }
    }
}
