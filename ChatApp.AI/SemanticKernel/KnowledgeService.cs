using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using ChatApp.AI.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Knowledge-base import, chunking, vectorization and retrieval (F5).</summary>
public class KnowledgeService : IKnowledgeService
{
    private const string Scope = "knowledge";
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectors;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        IDbContextFactory<AppDbContext> factory,
        IEmbeddingService embedding,
        IVectorStore vectors,
        ILogger<KnowledgeService> logger)
    {
        _factory = factory;
        _embedding = embedding;
        _vectors = vectors;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KnowledgeDocuments.AsNoTracking().OrderByDescending(d => d.ImportedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsByGroupAsync(int? groupId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<KnowledgeDocument> q = db.KnowledgeDocuments.AsNoTracking();
        if (groupId.HasValue)
            q = q.Where(d => d.GroupId == groupId.Value);
        else
            q = q.Where(d => d.GroupId == null);
        return await q.OrderByDescending(d => d.ImportedAt).ToListAsync(ct);
    }

    public async Task<KnowledgeDocument?> GetDocumentAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KnowledgeDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> GetChunksAsync(int documentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KnowledgeChunks.AsNoTracking().Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex).ToListAsync(ct);
    }

    public async Task<KnowledgeDocument> ImportAsync(string filePath, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default, int? groupId = null)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("文件不存在。", filePath);

        var text = await DocumentLoader.LoadAsync(filePath, ct);
        var charCount = text.Length;
        var chunks = TextChunker.Chunk(text);
        if (chunks.Count == 0) chunks = new List<string> { text };

        // Create document row first to obtain its Id.
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = new KnowledgeDocument
        {
            Title = DocumentLoader.DetectTitle(filePath),
            FileName = Path.GetFileName(filePath),
            FileType = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            CharCount = charCount,
            ChunkCount = chunks.Count,
            GroupId = groupId,
            ImportedAt = DateTime.UtcNow
        };
        db.KnowledgeDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        // Embed + store + persist chunk rows.
        var vectors = await _embedding.EmbedBatchAsync(chunks, ct);
        progress?.Report((0, chunks.Count));

        var chunkRows = new List<KnowledgeChunk>();
        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var externalId = $"doc{doc.Id}_chunk{i}";
            await _vectors.UpsertAsync(new VectorRecord
            {
                Id = externalId,
                Scope = Scope,
                Content = chunks[i],
                Embedding = vectors[i],
                Metadata = new() { ["documentId"] = doc.Id.ToString(), ["index"] = i.ToString() }
            }, ct);

            chunkRows.Add(new KnowledgeChunk
            {
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = chunks[i],
                ExternalId = externalId
            });
            progress?.Report((i + 1, chunks.Count));
        }

        await using var db2 = await _factory.CreateDbContextAsync(ct);
        db2.KnowledgeChunks.AddRange(chunkRows);
        await db2.SaveChangesAsync(ct);

        _logger.LogInformation("Imported '{File}' as doc {Id} with {Count} chunks.", doc.FileName, doc.Id, chunks.Count);
        return doc;
    }

    /// <summary>Supported file extensions (without the leading dot), kept in sync with <see cref="DocumentLoader"/>.</summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { "txt", "md", "markdown", "pdf" };

    public async Task<IReadOnlyList<KnowledgeDocument>> ImportDirectoryAsync(string directoryPath, bool recursive, IProgress<(int doneFiles, int totalFiles, string currentFile)>? progress = null, CancellationToken ct = default, int? groupId = null)
    {
        if (!Directory.Exists(directoryPath)) throw new DirectoryNotFoundException("文件夹不存在：" + directoryPath);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directoryPath, "*.*", searchOption)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).TrimStart('.')))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<KnowledgeDocument>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report((i, files.Count, Path.GetFileName(file)));
            try
            {
                var doc = await ImportAsync(file, null, ct, groupId);
                results.Add(doc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping '{File}' during directory import: {Message}", file, ex.Message);
            }
        }
        progress?.Report((files.Count, files.Count, string.Empty));
        _logger.LogInformation("Imported {Count} file(s) from directory '{Dir}' into group {GroupId}.", results.Count, directoryPath, groupId?.ToString() ?? "(none)");
        return results;
    }

    public async Task<KnowledgeRetrievalResult> RetrieveAsync(KnowledgeRetrievalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return KnowledgeRetrievalResult.NoRelevantMatch("查询为空");

        var groupIds = request.AllowedGroupIds.Where(id => id > 0).Distinct().ToArray();
        if (groupIds.Length == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("角色未绑定知识分组");

        var topK = Math.Clamp(request.TopK, 1, 50);
        var minScore = Math.Clamp(request.MinScore, 0, 1);
        var budget = Math.Clamp(request.ContextCharBudget, 200, 50_000);
        var neighborRadius = Math.Clamp(request.NeighborRadius, 0, 3);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.GroupId.HasValue && groupIds.Contains(d.GroupId.Value))
            .ToListAsync(ct);
        if (documents.Count == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("绑定的知识分组中没有文档");

        var documentIds = documents.Select(d => d.Id).ToArray();
        var chunks = await db.KnowledgeChunks.AsNoTracking()
            .Where(c => documentIds.Contains(c.DocumentId))
            .OrderBy(c => c.DocumentId)
            .ThenBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        var allowedIds = chunks.Where(c => !string.IsNullOrWhiteSpace(c.ExternalId))
            .Select(c => c.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        if (allowedIds.Count == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("绑定的知识分组中没有可检索分块");

        var queryVector = await _embedding.EmbedAsync(request.Query, ct);
        var directHits = await _vectors.SearchAsync(
            queryVector, Scope, topK, minScore, allowedIds, ct);
        if (directHits.Count == 0)
        {
            _logger.LogInformation(
                "Knowledge retrieval returned no match. Groups={Groups}, MinScore={MinScore:F3}",
                string.Join(',', groupIds), minScore);
            return KnowledgeRetrievalResult.NoRelevantMatch("没有达到相似度阈值的资料");
        }

        var documentMap = documents.ToDictionary(d => d.Id);
        var chunkByExternalId = chunks
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalId))
            .ToDictionary(c => c.ExternalId, StringComparer.Ordinal);
        var chunksByDocument = chunks.GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.ChunkIndex));

        var result = new List<KnowledgeHit>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        var remaining = budget;

        foreach (var direct in directHits)
        {
            if (!chunkByExternalId.TryGetValue(direct.Record.Id, out var directChunk)) continue;
            if (!documentMap.TryGetValue(directChunk.DocumentId, out var document)) continue;
            if (!chunksByDocument.TryGetValue(directChunk.DocumentId, out var documentChunks)) continue;

            var candidateIndices = new List<int> { directChunk.ChunkIndex };
            for (var offset = 1; offset <= neighborRadius; offset++)
            {
                candidateIndices.Add(directChunk.ChunkIndex - offset);
                candidateIndices.Add(directChunk.ChunkIndex + offset);
            }

            foreach (var index in candidateIndices)
            {
                if (remaining <= 0) break;
                if (!documentChunks.TryGetValue(index, out var chunk)) continue;
                if (!included.Add(chunk.ExternalId)) continue;

                var content = chunk.Content;
                if (content.Length > remaining)
                    content = content[..remaining];
                if (content.Length == 0) break;

                result.Add(new KnowledgeHit
                {
                    DocumentId = document.Id,
                    DocumentTitle = document.Title,
                    ChunkIndex = chunk.ChunkIndex,
                    Content = content,
                    Score = direct.Score,
                    IsDirectMatch = chunk.ChunkIndex == directChunk.ChunkIndex
                });
                remaining -= content.Length;
            }
        }

        if (result.Count == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("命中分块无法读取");

        _logger.LogInformation(
            "Knowledge retrieval found {Count} context chunk(s). Groups={Groups}, DirectHits={Hits}",
            result.Count,
            string.Join(',', groupIds),
            string.Join(',', directHits.Select(h => $"{h.Record.Id}:{h.Score:F3}")));
        return new KnowledgeRetrievalResult
        {
            Status = KnowledgeRetrievalStatus.Found,
            Hits = result
        };
    }

    public async Task DeleteDocumentAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(c => c.DocumentId == id).ToListAsync(ct);
        foreach (var ch in chunks)
        {
            if (!string.IsNullOrEmpty(ch.ExternalId))
                await _vectors.DeleteAsync(ch.ExternalId, ct);
        }
        db.KnowledgeChunks.RemoveRange(db.KnowledgeChunks.Where(c => c.DocumentId == id));
        var doc = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is not null) db.KnowledgeDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
    }

    // ----- 知识库分组管理 -----

    public async Task<IReadOnlyList<KnowledgeGroup>> ListGroupsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KnowledgeGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
    }

    public async Task<KnowledgeGroup> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("分组名不能为空。", nameof(name));

        await using var db = await _factory.CreateDbContextAsync(ct);
        // 检查重名
        if (await db.KnowledgeGroups.AnyAsync(g => g.Name == trimmed, ct))
            throw new InvalidOperationException($"已存在同名分组：「{trimmed}」");

        var group = new KnowledgeGroup { Name = trimmed, CreatedAt = DateTime.UtcNow };
        db.KnowledgeGroups.Add(group);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Created knowledge group {Id} '{Name}'.", group.Id, group.Name);
        return group;
    }

    public async Task RenameGroupAsync(int id, string newName, CancellationToken ct = default)
    {
        var trimmed = newName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("分组名不能为空。", nameof(newName));

        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.KnowledgeGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new KeyNotFoundException($"分组不存在：{id}");

        // 检查重名（排除自己）
        if (await db.KnowledgeGroups.AnyAsync(g => g.Name == trimmed && g.Id != id, ct))
            throw new InvalidOperationException($"已存在同名分组：「{trimmed}」");

        group.Name = trimmed;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Renamed knowledge group {Id} to '{Name}'.", id, trimmed);
    }

    public async Task DeleteGroupAsync(int id, bool deleteDocuments, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.KnowledgeGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new KeyNotFoundException($"分组不存在：{id}");

        var docs = await db.KnowledgeDocuments.Where(d => d.GroupId == id).ToListAsync(ct);
        db.RoleKnowledgeGroups.RemoveRange(db.RoleKnowledgeGroups.Where(x => x.KnowledgeGroupId == id));
        if (deleteDocuments)
        {
            // 一并删除组内所有文档及其向量、chunk
            foreach (var doc in docs)
            {
                var chunks = await db.KnowledgeChunks.AsNoTracking()
                    .Where(c => c.DocumentId == doc.Id).ToListAsync(ct);
                foreach (var ch in chunks)
                {
                    if (!string.IsNullOrEmpty(ch.ExternalId))
                        await _vectors.DeleteAsync(ch.ExternalId, ct);
                }
                db.KnowledgeChunks.RemoveRange(db.KnowledgeChunks.Where(c => c.DocumentId == doc.Id));
            }
            db.KnowledgeDocuments.RemoveRange(docs);
            _logger.LogInformation("Deleted {Count} document(s) along with group {Id}.", docs.Count, id);
        }
        else
        {
            // 把组内文档移到「未分组」
            foreach (var doc in docs) doc.GroupId = null;
            _logger.LogInformation("Moved {Count} document(s) to ungrouped after deleting group {Id}.", docs.Count, id);
        }

        db.KnowledgeGroups.Remove(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveDocumentAsync(int documentId, int? groupId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"文档不存在：{documentId}");

        if (groupId.HasValue)
        {
            // 校验目标分组确实存在
            var groupExists = await db.KnowledgeGroups.AnyAsync(g => g.Id == groupId.Value, ct);
            if (!groupExists) throw new KeyNotFoundException($"目标分组不存在：{groupId.Value}");
        }

        doc.GroupId = groupId;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Moved document {DocId} to group {GroupId}.", documentId, groupId?.ToString() ?? "(none)");
    }

    // ----- 批量操作 -----

    public async Task DeleteDocumentsAsync(IReadOnlyList<int> documentIds, CancellationToken ct = default)
    {
        if (documentIds is null || documentIds.Count == 0) return;

        await using var db = await _factory.CreateDbContextAsync(ct);

        // 1. 收集这些文档的所有 chunk 的 ExternalId，用于删除向量
        var chunks = await db.KnowledgeChunks.AsNoTracking()
            .Where(c => documentIds.Contains(c.DocumentId))
            .Select(c => new { c.DocumentId, c.ExternalId })
            .ToListAsync(ct);

        foreach (var ch in chunks)
        {
            if (!string.IsNullOrEmpty(ch.ExternalId))
                await _vectors.DeleteAsync(ch.ExternalId, ct);
        }

        // 2. 删除 chunk 行
        db.KnowledgeChunks.RemoveRange(db.KnowledgeChunks.Where(c => documentIds.Contains(c.DocumentId)));
        // 3. 删除文档行
        db.KnowledgeDocuments.RemoveRange(db.KnowledgeDocuments.Where(d => documentIds.Contains(d.Id)));

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Batch deleted {Count} document(s).", documentIds.Count);
    }

    public async Task MoveDocumentsAsync(IReadOnlyList<int> documentIds, int? groupId, CancellationToken ct = default)
    {
        if (documentIds is null || documentIds.Count == 0) return;

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (groupId.HasValue)
        {
            var groupExists = await db.KnowledgeGroups.AnyAsync(g => g.Id == groupId.Value, ct);
            if (!groupExists) throw new KeyNotFoundException($"目标分组不存在：{groupId.Value}");
        }

        var docs = await db.KnowledgeDocuments.Where(d => documentIds.Contains(d.Id)).ToListAsync(ct);
        foreach (var doc in docs) doc.GroupId = groupId;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Batch moved {Count} document(s) to group {GroupId}.", docs.Count, groupId?.ToString() ?? "(none)");
    }
}
