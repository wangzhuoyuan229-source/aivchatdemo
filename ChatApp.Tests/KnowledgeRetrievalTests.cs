using ChatApp.AI.SemanticKernel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Repositories;
using ChatApp.Infrastructure.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatApp.Tests;

public class KnowledgeRetrievalTests
{
    [Fact]
    public void VectorRankingFiltersAllowedIdsBeforeTopKAndAppliesThreshold()
    {
        var records = new[]
        {
            Record("foreign", 1, 0),
            Record("allowed-good", 0.8f, 0.6f),
            Record("allowed-low", 0.2f, 0.98f)
        };
        var allowed = new HashSet<string> { "allowed-good", "allowed-low" };

        var hits = SqliteVectorStore.Rank(records, new[] { 1f, 0f }, 1, 0.5, allowed);

        var hit = Assert.Single(hits);
        Assert.Equal("allowed-good", hit.Record.Id);
        Assert.InRange(hit.Score, 0.79, 0.81);
    }

    [Fact]
    public async Task RetrievalOnlyUsesBoundGroupsAndExpandsAdjacentChunks()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var factory = await CreateSeededFactoryAsync(databasePath);
            var vectors = new FakeVectorStore(new[]
            {
                Record("doc1_chunk0", 0.7f, 0.714f),
                Record("doc1_chunk1", 1, 0),
                Record("doc1_chunk2", 0.6f, 0.8f),
                Record("doc2_chunk0", 1, 0)
            });
            var service = new KnowledgeService(
                factory,
                new FakeEmbeddingService(),
                vectors,
                NullLogger<KnowledgeService>.Instance);

            var result = await service.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "月港在哪里",
                AllowedGroupIds = new[] { 1 },
                TopK = 1,
                MinScore = 0.35,
                NeighborRadius = 1,
                ContextCharBudget = 6000
            });

            Assert.Equal(KnowledgeRetrievalStatus.Found, result.Status);
            Assert.Equal(3, result.Hits.Count);
            Assert.All(result.Hits, h => Assert.Equal(1, h.DocumentId));
            Assert.Equal(1, result.Hits[0].ChunkIndex);
            Assert.True(result.Hits[0].IsDirectMatch);
            Assert.DoesNotContain(result.Hits, h => h.Content.Contains("另一个角色", StringComparison.Ordinal));
            Assert.DoesNotContain("doc2_chunk0", vectors.LastAllowedIds!);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task RetrievalRespectsContextBudgetAndReturnsNoMatchForUnboundRole()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var factory = await CreateSeededFactoryAsync(databasePath, new string('甲', 300));
            var service = new KnowledgeService(
                factory,
                new FakeEmbeddingService(),
                new FakeVectorStore(new[] { Record("doc1_chunk1", 1, 0) }),
                NullLogger<KnowledgeService>.Instance);

            var unbound = await service.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "问题",
                AllowedGroupIds = Array.Empty<int>()
            });
            var bounded = await service.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "问题",
                AllowedGroupIds = new[] { 1 },
                TopK = 1,
                MinScore = 0.35,
                NeighborRadius = 0,
                ContextCharBudget = 200
            });

            Assert.Equal(KnowledgeRetrievalStatus.NoRelevantMatch, unbound.Status);
            Assert.Equal(200, Assert.Single(bounded.Hits).Content.Length);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TwoGroupChatRolesReceiveDisjointKnowledgeContexts()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var factory = await CreateSeededFactoryAsync(databasePath);
            var vectors = new FakeVectorStore(new[]
            {
                Record("doc1_chunk1", 1, 0),
                Record("doc2_chunk0", 1, 0)
            });
            var knowledge = new KnowledgeService(
                factory,
                new FakeEmbeddingService(),
                vectors,
                NullLogger<KnowledgeService>.Instance);
            var roles = new RoleService(factory, NullLogger<RoleService>.Instance);

            var roleOne = await knowledge.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "你知道什么",
                AllowedGroupIds = await roles.GetKnowledgeGroupIdsAsync(1),
                TopK = 1,
                NeighborRadius = 0
            });
            var roleTwo = await knowledge.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "你知道什么",
                AllowedGroupIds = await roles.GetKnowledgeGroupIdsAsync(2),
                TopK = 1,
                NeighborRadius = 0
            });

            Assert.All(roleOne.Hits, h => Assert.Equal(1, h.DocumentId));
            Assert.All(roleTwo.Hits, h => Assert.Equal(2, h.DocumentId));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ImageRetrievalUsesIndependentScopeTopKAndGroupBoundary()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var factory = new TestDbContextFactory(databasePath);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.KnowledgeGroups.AddRange(
                    new KnowledgeGroup { Id = 1, Name = "可见" },
                    new KnowledgeGroup { Id = 2, Name = "不可见" });
                db.KnowledgeDocuments.AddRange(
                    new KnowledgeDocument
                    {
                        Id = 11, GroupId = 1, Kind = KnowledgeItemKind.Image, Title = "月港图",
                        FileName = "moon.png", StorageKey = "images/moon.png", MimeType = "image/png",
                        SourceRelativePath = "立绘/Y/月港/JPEG/立绘-月港-精二.png",
                        SemanticDescription = "月光下的港口", Tags = "月光,港口"
                    },
                    new KnowledgeDocument
                    {
                        Id = 22, GroupId = 2, Kind = KnowledgeItemKind.Image, Title = "沙漠图",
                        FileName = "sand.png", StorageKey = "images/sand.png", MimeType = "image/png",
                        SemanticDescription = "沙漠", Tags = "沙漠"
                    });
                db.KnowledgeChunks.AddRange(
                    new KnowledgeChunk { DocumentId = 11, ChunkIndex = 0, ExternalId = "image_doc11", Content = "月光下的港口" },
                    new KnowledgeChunk { DocumentId = 22, ChunkIndex = 0, ExternalId = "image_doc22", Content = "沙漠" });
                await db.SaveChangesAsync();
            }
            var vectors = new FakeVectorStore(new[]
            {
                new VectorRecord { Id = "image_doc11", Scope = "knowledge-image", Content = "月港", Embedding = new[] { 1f, 0f } },
                new VectorRecord { Id = "image_doc22", Scope = "knowledge-image", Content = "沙漠", Embedding = new[] { 1f, 0f } }
            });
            var service = new KnowledgeService(factory, new FakeEmbeddingService(), vectors, NullLogger<KnowledgeService>.Instance);

            var result = await service.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = "月港图片",
                AllowedGroupIds = new[] { 1 },
                ImageTopK = 1,
                ImageMinScore = 0.35
            });

            var image = Assert.Single(result.ImageHits);
            Assert.Equal(11, image.DocumentId);
            Assert.DoesNotContain("image_doc22", vectors.LastAllowedIds!);
            Assert.Empty(result.Hits);

            var avatarCandidates = await service.FindRoleAvatarCandidatesAsync("月港", new[] { 1 });
            var avatarCandidate = Assert.Single(avatarCandidates);
            Assert.Equal(11, avatarCandidate.DocumentId);
            Assert.Equal(1.0, avatarCandidate.Score);

            await service.UpdateImageMetadataAsync(11, "手动更新后的月港图片", "月港，夜景");
            Assert.NotNull(vectors.LastUpsert);
            Assert.Equal("knowledge-image", vectors.LastUpsert!.Scope);
            Assert.Contains("手动更新后的月港图片", vectors.LastUpsert.Content);
            await using var verify = await factory.CreateDbContextAsync();
            var updated = await verify.KnowledgeDocuments.AsNoTracking().SingleAsync(d => d.Id == 11);
            Assert.Equal(ImageDescriptionSource.Manual, updated.DescriptionSource);
            Assert.Equal("月港,夜景", updated.Tags);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static VectorRecord Record(string id, float x, float y) => new()
    {
        Id = id,
        Scope = "knowledge",
        Content = id,
        Embedding = new[] { x, y }
    };

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"chatapp-tests-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static async Task<TestDbContextFactory> CreateSeededFactoryAsync(
        string path,
        string? directContent = null)
    {
        var factory = new TestDbContextFactory(path);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.KnowledgeGroups.AddRange(
            new KnowledgeGroup { Id = 1, Name = "角色一" },
            new KnowledgeGroup { Id = 2, Name = "角色二" });
        db.Roles.AddRange(
            new Role { Id = 1, Name = "角色一" },
            new Role { Id = 2, Name = "角色二" });
        db.RoleKnowledgeGroups.AddRange(
            new RoleKnowledgeGroup { RoleId = 1, KnowledgeGroupId = 1 },
            new RoleKnowledgeGroup { RoleId = 2, KnowledgeGroupId = 2 });
        db.KnowledgeDocuments.AddRange(
            new KnowledgeDocument { Id = 1, GroupId = 1, Title = "月港设定", FileName = "a.md" },
            new KnowledgeDocument { Id = 2, GroupId = 2, Title = "其他设定", FileName = "b.md" });
        db.KnowledgeChunks.AddRange(
            new KnowledgeChunk { DocumentId = 1, ChunkIndex = 0, ExternalId = "doc1_chunk0", Content = "月港位于北海。" },
            new KnowledgeChunk { DocumentId = 1, ChunkIndex = 1, ExternalId = "doc1_chunk1", Content = directContent ?? "月港终年无雪。" },
            new KnowledgeChunk { DocumentId = 1, ChunkIndex = 2, ExternalId = "doc1_chunk2", Content = "港口只在清晨开放。" },
            new KnowledgeChunk { DocumentId = 2, ChunkIndex = 0, ExternalId = "doc2_chunk0", Content = "另一个角色住在沙漠。" });
        await db.SaveChangesAsync();
        return factory;
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

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1f, 0f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToList());
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        private readonly IReadOnlyList<VectorRecord> _records;
        public IReadOnlySet<string>? LastAllowedIds { get; private set; }
        public VectorRecord? LastUpsert { get; private set; }

        public FakeVectorStore(IReadOnlyList<VectorRecord> records) => _records = records;

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            float[] queryVector,
            string scope,
            int topK,
            double minScore = 0,
            IReadOnlySet<string>? allowedIds = null,
            CancellationToken ct = default)
        {
            LastAllowedIds = allowedIds;
            return Task.FromResult(SqliteVectorStore.Rank(_records, queryVector, topK, minScore, allowedIds));
        }

        public Task UpsertAsync(VectorRecord record, CancellationToken ct = default)
        {
            LastUpsert = record;
            return Task.CompletedTask;
        }
        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteByScopeAsync(string scope, CancellationToken ct = default) => Task.CompletedTask;
    }
}
