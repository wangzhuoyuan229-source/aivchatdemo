using System.Collections.Concurrent;
using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using ChatApp.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public sealed class KnowledgeFolderImportTests
{
    [Fact]
    public async Task RecursiveImportPersistsEveryDirectoryLevel()
    {
        var testRoot = NewTestRoot();
        try
        {
            var importRoot = Path.Combine(testRoot, "故事设定");
            var nested = Path.Combine(importRoot, "世界观", "地点", "北境");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(importRoot, "总览.md"), "# 故事总览\n这是总览。");
            await File.WriteAllTextAsync(Path.Combine(nested, "冰港.txt"), "冰港位于北境。");

            var service = await CreateServiceAsync(Path.Combine(testRoot, "knowledge.db"));
            var imported = await service.ImportDirectoryAsync(importRoot, recursive: true);

            Assert.Equal(2, imported.Count);
            Assert.Contains(imported, document => document.SourceRelativePath == "故事设定/总览.md");
            Assert.Contains(imported, document => document.SourceRelativePath == "故事设定/世界观/地点/北境/冰港.txt");
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MultiRootBatchKeepsRootsSeparateWhenNamesCollide()
    {
        var testRoot = NewTestRoot();
        try
        {
            var firstRoot = Path.Combine(testRoot, "甲", "资料");
            var secondRoot = Path.Combine(testRoot, "乙", "资料");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            await File.WriteAllTextAsync(Path.Combine(firstRoot, "人物.txt"), "人物甲");
            await File.WriteAllTextAsync(Path.Combine(secondRoot, "人物.txt"), "人物乙");

            var service = await CreateServiceAsync(Path.Combine(testRoot, "knowledge.db"));
            var imported = await service.ImportDirectoriesAsync(new[] { firstRoot, secondRoot }, recursive: true);

            Assert.Equal(2, imported.Count);
            Assert.Contains(imported, document => document.SourceRelativePath == "资料/人物.txt");
            Assert.Contains(imported, document => document.SourceRelativePath == "资料 (2)/人物.txt");
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReimportSkipsAlreadyIndexedFilesAndReportsByteProgress()
    {
        var testRoot = NewTestRoot();
        try
        {
            var importRoot = Path.Combine(testRoot, "可恢复导入");
            Directory.CreateDirectory(importRoot);
            await File.WriteAllTextAsync(Path.Combine(importRoot, "一.txt"), "第一份设定");
            await File.WriteAllTextAsync(Path.Combine(importRoot, "二.txt"), "第二份设定");
            var service = await CreateServiceAsync(Path.Combine(testRoot, "knowledge.db"));
            await service.ImportDirectoryAsync(importRoot, recursive: true);
            var progress = new ProgressRecorder<KnowledgeImportProgress>();

            var importedAgain = await service.ImportDirectoriesAsync(
                new[] { importRoot }, recursive: true, progress: progress);

            Assert.Empty(importedAgain);
            Assert.NotNull(progress.Last);
            Assert.Equal(2, progress.Last!.Completed);
            Assert.Equal(2, progress.Last.SkippedCount);
            Assert.Equal(progress.Last.TotalBytes, progress.Last.ProcessedBytes);
            Assert.Equal(2, (await service.ListDocumentsAsync()).Count);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryImportAcceptsExtensionlessTextButSkipsHiddenMetadataFiles()
    {
        var testRoot = NewTestRoot();
        try
        {
            var importRoot = Path.Combine(testRoot, "特殊资料");
            Directory.CreateDirectory(importRoot);
            await File.WriteAllTextAsync(Path.Combine(importRoot, "没有扩展名"), "可被索引的纯文本");
            await File.WriteAllBytesAsync(Path.Combine(importRoot, ".DS_Store"), new byte[] { 0, 1, 2, 3 });
            var service = await CreateServiceAsync(Path.Combine(testRoot, "knowledge.db"));

            var imported = await service.ImportDirectoryAsync(importRoot, recursive: true);

            var document = Assert.Single(imported);
            Assert.Equal("特殊资料/没有扩展名", document.SourceRelativePath);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void FolderTreeIncludesAncestorsAndCountsDescendants()
    {
        var documents = new List<KnowledgeDocument>
        {
            new() { Id = 1, FileName = "冰港.txt", SourceRelativePath = "故事设定/世界观/地点/冰港.txt" },
            new() { Id = 2, FileName = "人物.md", SourceRelativePath = "故事设定/人物/主角.md" },
            new() { Id = 3, FileName = "散页.txt", SourceRelativePath = "散页.txt" }
        };

        var nodes = KnowledgeFolderTree.Build(Array.Empty<KnowledgeGroup>(), documents);

        var root = Assert.Single(nodes, node => node.FolderPath == "故事设定");
        var world = Assert.Single(nodes, node => node.FolderPath == "故事设定/世界观");
        var places = Assert.Single(nodes, node => node.FolderPath == "故事设定/世界观/地点");
        Assert.Equal(2, root.DocumentCount);
        Assert.Equal(1, world.DocumentCount);
        Assert.Equal(1, places.DocumentCount);
        Assert.Equal(3, places.Depth);
        Assert.True(KnowledgeFolderTree.Contains(documents[0], world.FolderPath));
        Assert.False(KnowledgeFolderTree.Contains(documents[1], world.FolderPath));
    }

    private static string NewTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chatapp-folder-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<KnowledgeService> CreateServiceAsync(string databasePath)
    {
        var factory = new TestDbContextFactory(databasePath);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        return new KnowledgeService(
            factory,
            new FakeEmbeddingService(),
            new FakeVectorStore(),
            NullLogger<KnowledgeService>.Instance);
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

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
    }

    private sealed class ProgressRecorder<T> : IProgress<T>
    {
        public T? Last { get; private set; }

        public void Report(T value) => Last = value;
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
