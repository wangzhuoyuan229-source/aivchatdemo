using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using ChatApp.AI.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SkiaSharp;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Knowledge-base import, chunking, vectorization and retrieval (F5).</summary>
public class KnowledgeService : IKnowledgeService
{
    private const string TextScope = "knowledge";
    private const string ImageScope = "knowledge-image";
    private const long MaxImageFileBytes = 20L * 1024 * 1024;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectors;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly IImageDescriptionService? _imageDescriptions;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);

    public KnowledgeService(
        IDbContextFactory<AppDbContext> factory,
        IEmbeddingService embedding,
        IVectorStore vectors,
        ILogger<KnowledgeService> logger,
        IImageDescriptionService? imageDescriptions = null)
    {
        _factory = factory;
        _embedding = embedding;
        _vectors = vectors;
        _logger = logger;
        _imageDescriptions = imageDescriptions;
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

        if (IsImageExtension(filePath))
            return await ImportImageAsync(filePath, Path.GetFileName(filePath), groupId, progress, ct);

        return await ImportTextAsync(filePath, Path.GetFileName(filePath), progress, ct, groupId);
    }

    private async Task<KnowledgeDocument> ImportTextAsync(
        string filePath,
        string sourceRelativePath,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct,
        int? groupId)
    {

        var text = await DocumentLoader.LoadAsync(filePath, ct);
        var charCount = text.Length;
        var chunks = TextChunker.Chunk(text);
        if (chunks.Count == 0) chunks = new List<string> { text };

        // Network-bound embedding happens before acquiring the persistence lock,
        // allowing several files to be prepared concurrently without leaving
        // partial database rows when a provider rejects a request.
        var embeddings = await _embedding.EmbedBatchAsync(chunks, ct);
        if (embeddings.Count != chunks.Count)
            throw new InvalidDataException(
                $"Embedding 服务返回了 {embeddings.Count} 个向量，但文档包含 {chunks.Count} 个分块。");
        progress?.Report((0, chunks.Count));

        KnowledgeDocument? doc = null;
        var chunkRows = new List<KnowledgeChunk>();
        var vectorRecords = new List<VectorRecord>();
        await _persistenceGate.WaitAsync(ct);
        try
        {
            // Create the document only after every embedding batch succeeds.
            await using var db = await _factory.CreateDbContextAsync(ct);
            doc = new KnowledgeDocument
            {
                Title = DocumentLoader.DetectTitle(filePath),
                FileName = Path.GetFileName(filePath),
                FileType = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                CharCount = charCount,
                ChunkCount = chunks.Count,
                SourceRelativePath = NormalizeRelativePath(sourceRelativePath),
                GroupId = groupId,
                ImportedAt = DateTime.UtcNow
            };
            db.KnowledgeDocuments.Add(doc);
            await db.SaveChangesAsync(ct);

            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var externalId = $"doc{doc.Id}_chunk{i}";
                vectorRecords.Add(new VectorRecord
                {
                    Id = externalId,
                    Scope = TextScope,
                    Content = chunks[i],
                    Embedding = embeddings[i],
                    Metadata = new() { ["documentId"] = doc.Id.ToString(), ["index"] = i.ToString() }
                });

                chunkRows.Add(new KnowledgeChunk
                {
                    DocumentId = doc.Id,
                    ChunkIndex = i,
                    Content = chunks[i],
                    ExternalId = externalId
                });
                progress?.Report((i + 1, chunks.Count));
            }
            await _vectors.UpsertBatchAsync(vectorRecords, ct);
            db.KnowledgeChunks.AddRange(chunkRows);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var record in vectorRecords)
            {
                try { await _vectors.DeleteAsync(record.Id, CancellationToken.None); }
                catch { /* best-effort compensation */ }
            }
            if (doc is not null)
            {
                try
                {
                    await using var cleanup = await _factory.CreateDbContextAsync(CancellationToken.None);
                    cleanup.KnowledgeChunks.RemoveRange(cleanup.KnowledgeChunks.Where(c => c.DocumentId == doc.Id));
                    cleanup.KnowledgeDocuments.RemoveRange(cleanup.KnowledgeDocuments.Where(d => d.Id == doc.Id));
                    await cleanup.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogError(cleanupError, "Failed to clean partial knowledge document {DocumentId}.", doc.Id);
                }
            }
            throw;
        }
        finally
        {
            _persistenceGate.Release();
        }

        _logger.LogInformation("Imported '{File}' as doc {Id} with {Count} chunks.", doc!.FileName, doc.Id, chunks.Count);
        return doc!;
    }

    /// <summary>Supported file extensions (without the leading dot), kept in sync with <see cref="DocumentLoader"/>.</summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "", "txt", "md", "markdown", "pdf", "png", "jpg", "jpeg", "webp" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "webp" };

    public Task<IReadOnlyList<KnowledgeDocument>> ImportFilesAsync(
        IReadOnlyList<string> filePaths,
        IProgress<KnowledgeImportProgress>? progress = null,
        CancellationToken ct = default,
        int? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var items = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => (path, relativePath: Path.GetFileName(path)))
            .ToList();
        return ImportManyAsync(items, progress, ct, groupId);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ImportDirectoryAsync(
        string directoryPath,
        bool recursive,
        IProgress<(int doneFiles, int totalFiles, string currentFile)>? progress = null,
        CancellationToken ct = default,
        int? groupId = null)
    {
        var structuredProgress = progress is null
            ? null
            : new Progress<KnowledgeImportProgress>(p => progress.Report((p.Completed, p.Total, p.CurrentFile)));
        var results = await ImportDirectoriesAsync(
            new[] { directoryPath }, recursive, structuredProgress, ct, groupId);
        _logger.LogInformation("Imported {Count} file(s) from directory '{Dir}' into group {GroupId}.", results.Count, directoryPath, groupId?.ToString() ?? "(none)");
        return results;
    }

    public Task<IReadOnlyList<KnowledgeDocument>> ImportDirectoriesAsync(
        IReadOnlyList<string> directoryPaths,
        bool recursive,
        IProgress<KnowledgeImportProgress>? progress = null,
        CancellationToken ct = default,
        int? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(directoryPaths);
        var roots = directoryPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var root in roots)
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("文件夹不存在：" + root);

        var items = new List<(string path, string relativePath)>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedRootNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = new DirectoryInfo(root).Name;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "导入目录";
            var occurrence = usedRootNames.TryGetValue(baseName, out var count) ? count + 1 : 1;
            usedRootNames[baseName] = occurrence;
            var rootName = occurrence == 1 ? baseName : $"{baseName} ({occurrence})";
            items.AddRange(Directory.EnumerateFiles(root, "*", enumerationOptions)
                .Where(IsSupportedImportFile)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Where(file => seenFiles.Add(Path.GetFullPath(file)))
                .Select(file => (
                    file,
                    NormalizeRelativePath(Path.Combine(rootName, Path.GetRelativePath(root, file))))));
        }

        return ImportManyAsync(items, progress, ct, groupId);
    }

    private async Task<IReadOnlyList<KnowledgeDocument>> ImportManyAsync(
        IReadOnlyList<(string path, string relativePath)> items,
        IProgress<KnowledgeImportProgress>? progress,
        CancellationToken ct,
        int? groupId)
    {
        var indexedPaths = await GetIndexedRelativePathsAsync(groupId, ct);
        var pendingItems = items
            .Where(item => !indexedPaths.Contains(NormalizeRelativePath(item.relativePath)))
            .ToList();
        var skipped = items.Count - pendingItems.Count;
        var totalBytes = items.Sum(item => GetFileSize(item.path));
        long processedBytes = items
            .Where(item => indexedPaths.Contains(NormalizeRelativePath(item.relativePath)))
            .Sum(item => GetFileSize(item.path));
        var results = new KnowledgeDocument?[pendingItems.Count];
        var completed = skipped;
        var succeeded = 0;
        var failed = 0;
        var fallback = 0;
        string? lastError = null;
        progress?.Report(new KnowledgeImportProgress
        {
            Stage = pendingItems.Count == 0 ? KnowledgeImportStage.Completed : KnowledgeImportStage.Scanning,
            Completed = completed,
            Total = items.Count,
            SkippedCount = skipped,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes
        });

        await Parallel.ForEachAsync(
            pendingItems.Select((item, index) => (item.path, item.relativePath, index)),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    progress?.Report(new KnowledgeImportProgress
                    {
                        Stage = IsImageExtension(item.path) ? KnowledgeImportStage.Describing : KnowledgeImportStage.Embedding,
                        Completed = Volatile.Read(ref completed),
                        Total = items.Count,
                        Succeeded = Volatile.Read(ref succeeded),
                        Failed = Volatile.Read(ref failed),
                        FallbackCount = Volatile.Read(ref fallback),
                        SkippedCount = skipped,
                        ProcessedBytes = Interlocked.Read(ref processedBytes),
                        TotalBytes = totalBytes,
                        CurrentFile = item.relativePath
                    });
                    var document = IsImageExtension(item.path)
                        ? await ImportImageAsync(item.path, item.relativePath, groupId, null, token)
                        : await ImportTextAsync(item.path, item.relativePath, null, token, groupId);
                    results[item.index] = document;
                    Interlocked.Increment(ref succeeded);
                    if (document.DescriptionSource == ImageDescriptionSource.MetadataFallback)
                        Interlocked.Increment(ref fallback);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Volatile.Write(ref lastError, FormatImportError(item.relativePath, ex));
                    _logger.LogWarning(ex, "Skipping '{File}' during batch import: {Message}", item.path, ex.Message);
                }
                finally
                {
                    Interlocked.Add(ref processedBytes, GetFileSize(item.path));
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new KnowledgeImportProgress
                    {
                        Stage = done == items.Count ? KnowledgeImportStage.Completed : KnowledgeImportStage.Persisting,
                        Completed = done,
                        Total = items.Count,
                        Succeeded = Volatile.Read(ref succeeded),
                        Failed = Volatile.Read(ref failed),
                        FallbackCount = Volatile.Read(ref fallback),
                        SkippedCount = skipped,
                        ProcessedBytes = Interlocked.Read(ref processedBytes),
                        TotalBytes = totalBytes,
                        CurrentFile = done == items.Count ? string.Empty : item.relativePath,
                        LastError = Volatile.Read(ref lastError) ?? string.Empty
                    });
                }
            });

        return results.Where(x => x is not null).Select(x => x!).ToList();
    }

    private async Task<HashSet<string>> GetIndexedRelativePathsAsync(int? groupId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<KnowledgeDocument> documents = db.KnowledgeDocuments.AsNoTracking();
        documents = groupId.HasValue
            ? documents.Where(document => document.GroupId == groupId.Value)
            : documents.Where(document => document.GroupId == null);
        var paths = await documents
            .Where(document => document.SourceRelativePath != string.Empty)
            .Where(document => db.KnowledgeChunks.Any(chunk => chunk.DocumentId == document.Id))
            .Select(document => document.SourceRelativePath)
            .ToListAsync(ct);
        return paths.Select(NormalizeRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<KnowledgeDocument> ImportImageAsync(
        string filePath,
        string sourceRelativePath,
        int? groupId,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        ValidateImage(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var storageKey = $"images/{Guid.NewGuid():N}{extension}";
        var destination = AppPaths.ResolveKnowledgeStorageKey(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report((0, 4));

        try
        {
            await using (var source = File.OpenRead(filePath))
            await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(target, ct);
            progress?.Report((1, 4));

            var fileName = Path.GetFileName(filePath);
            var description = _imageDescriptions is null
                ? BuildMetadataDescription(fileName, sourceRelativePath)
                : await _imageDescriptions.DescribeAsync(destination, fileName, sourceRelativePath, ct);
            progress?.Report((2, 4));

            var semanticContent = BuildImageSemanticContent(fileName, sourceRelativePath, description.Description, description.Tags);
            var embedding = await _embedding.EmbedAsync(semanticContent, ct);
            progress?.Report((3, 4));

            await _persistenceGate.WaitAsync(ct);
            try
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var doc = new KnowledgeDocument
                {
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    FileName = fileName,
                    FileType = extension.TrimStart('.'),
                    FileSize = new FileInfo(filePath).Length,
                    CharCount = semanticContent.Length,
                    ChunkCount = 1,
                    Kind = KnowledgeItemKind.Image,
                    StorageKey = storageKey,
                    MimeType = GetImageMimeType(extension),
                    SemanticDescription = description.Description,
                    Tags = description.Tags,
                    DescriptionSource = description.Source,
                    DescriptionProvider = description.Provider,
                    DescriptionModel = description.Model,
                    SourceRelativePath = NormalizeRelativePath(sourceRelativePath),
                    GroupId = groupId,
                    ImportedAt = DateTime.UtcNow
                };
                db.KnowledgeDocuments.Add(doc);
                await db.SaveChangesAsync(ct);

                var externalId = $"image_doc{doc.Id}";
                try
                {
                    await _vectors.UpsertAsync(new VectorRecord
                    {
                        Id = externalId,
                        Scope = ImageScope,
                        Content = semanticContent,
                        Embedding = embedding,
                        Metadata = new() { ["documentId"] = doc.Id.ToString(), ["kind"] = "image" }
                    }, ct);
                    db.KnowledgeChunks.Add(new KnowledgeChunk
                    {
                        DocumentId = doc.Id,
                        ChunkIndex = 0,
                        Content = semanticContent,
                        ExternalId = externalId
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch
                {
                    try { await _vectors.DeleteAsync(externalId, CancellationToken.None); }
                    catch { /* best-effort vector compensation */ }
                    try
                    {
                        db.ChangeTracker.Clear();
                        db.KnowledgeChunks.RemoveRange(db.KnowledgeChunks.Where(c => c.DocumentId == doc.Id));
                        db.KnowledgeDocuments.RemoveRange(db.KnowledgeDocuments.Where(d => d.Id == doc.Id));
                        await db.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception cleanupError)
                    {
                        _logger.LogError(cleanupError, "Failed to clean partial image knowledge item {DocumentId}.", doc.Id);
                    }
                    throw;
                }

                progress?.Report((4, 4));
                _logger.LogInformation("Imported image '{File}' as knowledge item {Id}.", fileName, doc.Id);
                return doc;
            }
            finally
            {
                _persistenceGate.Release();
            }
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }
    }

    public async Task<KnowledgeRetrievalResult> RetrieveAsync(KnowledgeRetrievalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return KnowledgeRetrievalResult.NoRelevantMatch("查询为空");

        var groupIds = request.AllowedGroupIds.Where(id => id > 0).Distinct().ToArray();
        if (groupIds.Length == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("角色未绑定知识分组");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.GroupId.HasValue && groupIds.Contains(d.GroupId.Value))
            .ToListAsync(ct);
        if (documents.Count == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("绑定的知识分组中没有知识项");

        var documentIds = documents.Select(d => d.Id).ToArray();
        var chunks = await db.KnowledgeChunks.AsNoTracking()
            .Where(c => documentIds.Contains(c.DocumentId))
            .OrderBy(c => c.DocumentId)
            .ThenBy(c => c.ChunkIndex)
            .ToListAsync(ct);
        if (chunks.Count == 0)
            return KnowledgeRetrievalResult.NoRelevantMatch("绑定的知识分组中没有可检索内容");

        var documentMap = documents.ToDictionary(d => d.Id);
        var chunkByExternalId = chunks.Where(c => !string.IsNullOrWhiteSpace(c.ExternalId))
            .ToDictionary(c => c.ExternalId, StringComparer.Ordinal);
        var textAllowedIds = chunks
            .Where(c => documentMap.TryGetValue(c.DocumentId, out var doc) && doc.Kind == KnowledgeItemKind.TextDocument)
            .Select(c => c.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var imageAllowedIds = chunks
            .Where(c => documentMap.TryGetValue(c.DocumentId, out var doc) && doc.Kind == KnowledgeItemKind.Image)
            .Select(c => c.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var queryVector = await _embedding.EmbedAsync(request.Query, ct);
        var textHits = textAllowedIds.Count == 0
            ? Array.Empty<VectorSearchHit>()
            : await _vectors.SearchAsync(
                queryVector,
                TextScope,
                Math.Clamp(request.TopK, 1, 50),
                Math.Clamp(request.MinScore, 0, 1),
                textAllowedIds,
                ct);
        var directImageHits = imageAllowedIds.Count == 0
            ? Array.Empty<VectorSearchHit>()
            : await _vectors.SearchAsync(
                queryVector,
                ImageScope,
                Math.Clamp(request.ImageTopK, 1, 20),
                Math.Clamp(request.ImageMinScore, 0, 1),
                imageAllowedIds,
                ct);

        var textResult = BuildTextHits(
            textHits,
            documents,
            chunks,
            Math.Clamp(request.ContextCharBudget, 200, 50_000),
            Math.Clamp(request.NeighborRadius, 0, 3));
        var imageResult = new List<KnowledgeImageHit>();
        foreach (var hit in directImageHits)
        {
            if (!chunkByExternalId.TryGetValue(hit.Record.Id, out var chunk)) continue;
            if (!documentMap.TryGetValue(chunk.DocumentId, out var document) || document.Kind != KnowledgeItemKind.Image) continue;
            if (string.IsNullOrWhiteSpace(document.StorageKey)) continue;
            imageResult.Add(new KnowledgeImageHit
            {
                DocumentId = document.Id,
                Title = document.Title,
                FileName = document.FileName,
                SourceRelativePath = document.SourceRelativePath,
                Description = document.SemanticDescription,
                Tags = document.Tags,
                StorageKey = document.StorageKey,
                MimeType = document.MimeType,
                Score = hit.Score
            });
        }

        if (textResult.Count == 0 && imageResult.Count == 0)
        {
            _logger.LogInformation("Knowledge retrieval returned no match. Groups={Groups}", string.Join(',', groupIds));
            return KnowledgeRetrievalResult.NoRelevantMatch("没有达到相似度阈值的资料或图片");
        }

        _logger.LogInformation(
            "Knowledge retrieval found {TextCount} text chunk(s) and {ImageCount} image(s). Groups={Groups}",
            textResult.Count,
            imageResult.Count,
            string.Join(',', groupIds));
        return new KnowledgeRetrievalResult
        {
            Status = KnowledgeRetrievalStatus.Found,
            Hits = textResult,
            ImageHits = imageResult
        };
    }

    private static IReadOnlyList<KnowledgeHit> BuildTextHits(
        IReadOnlyList<VectorSearchHit> directHits,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<KnowledgeChunk> chunks,
        int budget,
        int neighborRadius)
    {
        var documentMap = documents.ToDictionary(d => d.Id);
        var chunkByExternalId = chunks.Where(c => !string.IsNullOrWhiteSpace(c.ExternalId))
            .ToDictionary(c => c.ExternalId, StringComparer.Ordinal);
        var chunksByDocument = chunks.GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.ChunkIndex));
        var result = new List<KnowledgeHit>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        var remaining = budget;

        foreach (var direct in directHits)
        {
            if (!chunkByExternalId.TryGetValue(direct.Record.Id, out var directChunk)) continue;
            if (!documentMap.TryGetValue(directChunk.DocumentId, out var document) || document.Kind != KnowledgeItemKind.TextDocument) continue;
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
                if (!documentChunks.TryGetValue(index, out var chunk) || !included.Add(chunk.ExternalId)) continue;
                var content = chunk.Content.Length > remaining ? chunk.Content[..remaining] : chunk.Content;
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
        return result;
    }

    public async Task UpdateImageMetadataAsync(
        int documentId,
        string description,
        string tags,
        CancellationToken ct = default)
    {
        var normalizedDescription = description?.Trim() ?? string.Empty;
        if (normalizedDescription.Length == 0)
            throw new ArgumentException("图片描述不能为空。", nameof(description));
        await UpdateImageSemanticAsync(
            documentId,
            normalizedDescription,
            NormalizeTags(tags),
            ImageDescriptionSource.Manual,
            string.Empty,
            string.Empty,
            ct);
    }

    public async Task<KnowledgeDocument> RegenerateImageDescriptionAsync(int documentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var document = await db.KnowledgeDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"知识项不存在：{documentId}");
        if (document.Kind != KnowledgeItemKind.Image)
            throw new InvalidOperationException("只有图片知识项可以重新识图。");
        if (_imageDescriptions is null)
            throw new InvalidOperationException("图片识别服务不可用。");

        var path = AppPaths.ResolveKnowledgeStorageKey(document.StorageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("知识图片原文件不存在。", path);
        var result = await _imageDescriptions.DescribeAsync(
            path,
            document.FileName,
            document.SourceRelativePath,
            ct);
        await UpdateImageSemanticAsync(
            documentId,
            result.Description,
            result.Tags,
            result.Source,
            result.Provider,
            result.Model,
            ct);
        return await GetDocumentAsync(documentId, ct)
            ?? throw new InvalidOperationException("重新识图后无法读取知识项。");
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> RegenerateImageDescriptionsAsync(
        IReadOnlyCollection<int> documentIds,
        IProgress<KnowledgeImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        var ids = documentIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0) return Array.Empty<KnowledgeDocument>();

        var results = new KnowledgeDocument?[ids.Length];
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        var fallback = 0;
        string? lastError = null;
        progress?.Report(new KnowledgeImportProgress
        {
            Stage = KnowledgeImportStage.Describing,
            Total = ids.Length
        });

        await Parallel.ForEachAsync(
            ids.Select((id, index) => (id, index)),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    var document = await RegenerateImageDescriptionAsync(item.id, token);
                    results[item.index] = document;
                    Interlocked.Increment(ref succeeded);
                    if (document.DescriptionSource == ImageDescriptionSource.MetadataFallback)
                        Interlocked.Increment(ref fallback);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Volatile.Write(ref lastError, FormatImportError($"图片 #{item.id}", ex));
                    _logger.LogWarning(ex, "Skipping knowledge image {DocumentId} during batch recognition: {Message}", item.id, ex.Message);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new KnowledgeImportProgress
                    {
                        Stage = done == ids.Length ? KnowledgeImportStage.Completed : KnowledgeImportStage.Persisting,
                        Completed = done,
                        Total = ids.Length,
                        Succeeded = Volatile.Read(ref succeeded),
                        Failed = Volatile.Read(ref failed),
                        FallbackCount = Volatile.Read(ref fallback),
                        LastError = Volatile.Read(ref lastError) ?? string.Empty
                    });
                }
            });

        return results.Where(document => document is not null).Select(document => document!).ToList();
    }

    private async Task UpdateImageSemanticAsync(
        int documentId,
        string description,
        string tags,
        ImageDescriptionSource source,
        string provider,
        string model,
        CancellationToken ct)
    {
        await using var readDb = await _factory.CreateDbContextAsync(ct);
        var existing = await readDb.KnowledgeDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new KeyNotFoundException($"知识项不存在：{documentId}");
        if (existing.Kind != KnowledgeItemKind.Image)
            throw new InvalidOperationException("只有图片知识项可以修改图片语义。");

        var semantic = BuildImageSemanticContent(
            existing.FileName,
            existing.SourceRelativePath,
            description,
            tags);
        var embedding = await _embedding.EmbedAsync(semantic, ct);

        await _persistenceGate.WaitAsync(ct);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var document = await db.KnowledgeDocuments.FirstAsync(d => d.Id == documentId, ct);
            var chunk = await db.KnowledgeChunks.FirstOrDefaultAsync(c => c.DocumentId == documentId, ct)
                ?? throw new InvalidOperationException("图片知识项缺少索引分块。");
            document.SemanticDescription = description;
            document.Tags = tags;
            document.DescriptionSource = source;
            document.DescriptionProvider = provider;
            document.DescriptionModel = model;
            document.CharCount = semantic.Length;
            chunk.Content = semantic;
            await _vectors.UpsertAsync(new VectorRecord
            {
                Id = chunk.ExternalId,
                Scope = ImageScope,
                Content = semantic,
                Embedding = embedding,
                Metadata = new() { ["documentId"] = documentId.ToString(), ["kind"] = "image" }
            }, ct);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async Task<IReadOnlyList<MessageAttachment>> CreateMessageAttachmentSnapshotsAsync(
        IReadOnlyCollection<int> imageDocumentIds,
        CancellationToken ct = default)
    {
        if (imageDocumentIds.Count == 0) return Array.Empty<MessageAttachment>();
        var orderedIds = imageDocumentIds.Distinct().Take(3).ToList();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => orderedIds.Contains(d.Id) && d.Kind == KnowledgeItemKind.Image)
            .ToListAsync(ct);
        var byId = documents.ToDictionary(d => d.Id);
        var attachments = new List<MessageAttachment>();
        try
        {
            foreach (var id in orderedIds)
            {
                if (!byId.TryGetValue(id, out var document)) continue;
                var source = AppPaths.ResolveKnowledgeStorageKey(document.StorageKey);
                if (!File.Exists(source)) continue;
                var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
                if (extension.Length == 0) extension = ".img";
                var storageKey = $"{Guid.NewGuid():N}{extension}";
                var destination = AppPaths.ResolveMessageAttachmentStorageKey(storageKey);
                try
                {
                    await using var input = File.OpenRead(source);
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    await input.CopyToAsync(output, ct);
                    attachments.Add(new MessageAttachment
                    {
                        StorageKey = storageKey,
                        MimeType = document.MimeType,
                        FileName = document.FileName,
                        Title = document.Title,
                        Caption = document.SemanticDescription,
                        SourceKnowledgeDocumentId = document.Id
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    TryDeleteFile(destination);
                    throw;
                }
                catch (Exception ex)
                {
                    TryDeleteFile(destination);
                    _logger.LogWarning(ex, "Failed to snapshot knowledge image {DocumentId}.", document.Id);
                }
            }
        }
        catch
        {
            foreach (var attachment in attachments)
                TryDeleteFile(AppPaths.ResolveMessageAttachmentStorageKey(attachment.StorageKey));
            throw;
        }
        return attachments;
    }

    public async Task<string> CreateRoleAvatarDataUriAsync(
        int imageDocumentId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var document = await db.KnowledgeDocuments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == imageDocumentId, ct)
            ?? throw new KeyNotFoundException($"知识图片不存在：{imageDocumentId}");
        if (document.Kind != KnowledgeItemKind.Image || string.IsNullOrWhiteSpace(document.StorageKey))
            throw new InvalidOperationException("只能将图片知识项用作角色头像。");

        var path = AppPaths.ResolveKnowledgeStorageKey(document.StorageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("知识图片原文件不存在。", path);
        return await CreateRoleAvatarDataUriFromFileCoreAsync(
            path,
            $"{document.Title} {document.FileName}",
            ct);
    }

    public async Task<string> CreateRoleAvatarDataUriFromFileAsync(
        string imagePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException("头像图片路径不能为空。", nameof(imagePath));
        var path = Path.GetFullPath(imagePath);
        if (!File.Exists(path)) throw new FileNotFoundException("头像图片不存在。", path);
        return await CreateRoleAvatarDataUriFromFileCoreAsync(path, Path.GetFileName(path), ct);
    }

    private async Task<string> CreateRoleAvatarDataUriFromFileCoreAsync(
        string path,
        string subjectHint,
        CancellationToken ct)
    {
        ImageFaceRegion? faceRegion = null;
        if (_imageDescriptions is not null)
            faceRegion = await _imageDescriptions.LocatePrimaryFaceAsync(path, subjectHint, ct);
        var bytes = await Task.Run(() => CreateSquareAvatarJpeg(path, faceRegion: faceRegion), ct);
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }

    public async Task<IReadOnlyList<KnowledgeImageHit>> FindRoleAvatarCandidatesAsync(
        string roleName,
        IReadOnlyCollection<int> allowedGroupIds,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        var normalizedName = NormalizeAvatarMatchText(roleName);
        var groupIds = allowedGroupIds.Where(id => id > 0).Distinct().ToArray();
        if (normalizedName.Length == 0 || groupIds.Length == 0) return Array.Empty<KnowledgeImageHit>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(document => document.Kind == KnowledgeItemKind.Image &&
                               document.GroupId.HasValue && groupIds.Contains(document.GroupId.Value) &&
                               document.StorageKey != "")
            .ToListAsync(ct);

        return documents
            .Select(document => new
            {
                Document = document,
                MatchScore = RoleAvatarPathMatchScore(document, normalizedName),
                ArtworkScore = RoleAvatarArtworkPreference(document, normalizedName)
            })
            .Where(item => item.MatchScore > 0)
            .OrderByDescending(item => item.MatchScore)
            .ThenByDescending(item => item.ArtworkScore)
            .ThenBy(item => item.Document.SourceRelativePath.Length)
            .Take(Math.Clamp(maxResults, 1, 100))
            .Select(item => new KnowledgeImageHit
            {
                DocumentId = item.Document.Id,
                Title = item.Document.Title,
                FileName = item.Document.FileName,
                SourceRelativePath = item.Document.SourceRelativePath,
                Description = item.Document.SemanticDescription,
                Tags = item.Document.Tags,
                StorageKey = item.Document.StorageKey,
                MimeType = item.Document.MimeType,
                Score = item.MatchScore
            })
            .ToArray();
    }

    private static double RoleAvatarPathMatchScore(KnowledgeDocument document, string normalizedName)
    {
        var path = NormalizeRelativePath(document.SourceRelativePath);
        var folderSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1)
            .Select(NormalizeAvatarMatchText).ToArray();
        if (folderSegments.Any(segment => segment == normalizedName)) return 1.0;
        if (folderSegments.Any(segment => segment.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))) return 0.98;

        var title = NormalizeAvatarMatchText(document.Title);
        var fileName = NormalizeAvatarMatchText(Path.GetFileNameWithoutExtension(document.FileName));
        if (title == normalizedName || fileName == normalizedName) return 0.97;
        if (title.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) return 0.95;
        if (NormalizeAvatarMatchText(document.Tags).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) return 0.88;
        if (NormalizeAvatarMatchText(document.SemanticDescription).Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) return 0.82;
        return 0;
    }

    private static double RoleAvatarArtworkPreference(KnowledgeDocument document, string normalizedName)
    {
        var file = NormalizeAvatarMatchText(Path.GetFileNameWithoutExtension(document.FileName));
        var score = 0d;
        if (file == $"立绘{normalizedName}精二") score += 100;
        else if (file.Contains("精二", StringComparison.OrdinalIgnoreCase)) score += 30;
        else if (file.Contains("精一", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (file.Contains("残余", StringComparison.OrdinalIgnoreCase)) score -= 50;
        score -= NormalizeRelativePath(document.SourceRelativePath).Count(character => character == '/');
        return score;
    }

    private static string NormalizeAvatarMatchText(string value) => string.Concat(
        (value ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    internal static byte[] CreateSquareAvatarJpeg(
        string path,
        int size = 256,
        ImageFaceRegion? faceRegion = null)
    {
        if (size is < 64 or > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var normalized = ImageDescriptionService.NormalizeToJpeg(path);
        using var source = SKBitmap.Decode(normalized) ?? throw new InvalidDataException("无法解码头像图片。");
        var crop = ResolveAvatarCrop(source.Width, source.Height, faceRegion);
        using var avatar = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(avatar))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                source,
                crop,
                new SKRect(0, 0, size, size),
                paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(avatar);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 88)
            ?? throw new InvalidDataException("无法编码头像图片。");
        return encoded.ToArray();
    }

    internal static SKRect ResolveAvatarCrop(
        int imageWidth,
        int imageHeight,
        ImageFaceRegion? faceRegion)
    {
        if (imageWidth <= 0 || imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));

        if (faceRegion is not null)
        {
            var faceLeft = (float)(Math.Clamp(faceRegion.Left, 0, 1) * imageWidth);
            var faceTop = (float)(Math.Clamp(faceRegion.Top, 0, 1) * imageHeight);
            var faceRight = (float)(Math.Clamp(faceRegion.Right, 0, 1) * imageWidth);
            var faceBottom = (float)(Math.Clamp(faceRegion.Bottom, 0, 1) * imageHeight);
            var faceWidth = faceRight - faceLeft;
            var faceHeight = faceBottom - faceTop;
            if (faceWidth >= 2 && faceHeight >= 2)
            {
                // Retain some hair, ears and chin while making the head occupy most
                // of the avatar instead of preserving the full-body composition.
                var cropSize = Math.Clamp(
                    Math.Max(faceWidth * 1.75f, faceHeight * 1.6f),
                    32f,
                    Math.Min(imageWidth, imageHeight));
                var centerX = (faceLeft + faceRight) / 2f;
                var centerY = (faceTop + faceBottom) / 2f - faceHeight * 0.06f;
                var left = Math.Clamp(centerX - cropSize / 2f, 0, imageWidth - cropSize);
                var top = Math.Clamp(centerY - cropSize / 2f, 0, imageHeight - cropSize);
                return new SKRect(left, top, left + cropSize, top + cropSize);
            }
        }

        var fallbackSize = Math.Min(imageWidth, imageHeight);
        var fallbackLeft = (imageWidth - fallbackSize) / 2f;
        // Character illustrations are commonly tall full-body images. Bias a portrait
        // crop toward the upper part of the source so the face is less likely to be cut.
        var fallbackTop = imageHeight > imageWidth
            ? (imageHeight - fallbackSize) * 0.12f
            : (imageHeight - fallbackSize) / 2f;
        return new SKRect(
            fallbackLeft,
            fallbackTop,
            fallbackLeft + fallbackSize,
            fallbackTop + fallbackSize);
    }

    public async Task DeleteDocumentAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        var storagePath = doc is { Kind: KnowledgeItemKind.Image } && !string.IsNullOrWhiteSpace(doc.StorageKey)
            ? AppPaths.ResolveKnowledgeStorageKey(doc.StorageKey)
            : null;
        var chunks = await db.KnowledgeChunks.AsNoTracking().Where(c => c.DocumentId == id).ToListAsync(ct);
        foreach (var ch in chunks)
        {
            if (!string.IsNullOrEmpty(ch.ExternalId))
                await _vectors.DeleteAsync(ch.ExternalId, ct);
        }
        db.KnowledgeChunks.RemoveRange(db.KnowledgeChunks.Where(c => c.DocumentId == id));
        if (doc is not null) db.KnowledgeDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
        if (storagePath is not null) TryDeleteFile(storagePath);
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
        var imagePaths = docs.Where(d => d.Kind == KnowledgeItemKind.Image && !string.IsNullOrWhiteSpace(d.StorageKey))
            .Select(d => AppPaths.ResolveKnowledgeStorageKey(d.StorageKey))
            .ToList();
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
        if (deleteDocuments)
            foreach (var path in imagePaths) TryDeleteFile(path);
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

        var imagePaths = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id) && d.Kind == KnowledgeItemKind.Image && d.StorageKey != "")
            .Select(d => d.StorageKey)
            .ToListAsync(ct);

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
        foreach (var storageKey in imagePaths)
            TryDeleteFile(AppPaths.ResolveKnowledgeStorageKey(storageKey));
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

    private static bool IsImageExtension(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).TrimStart('.'));

    private static bool IsSupportedImportFile(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        return SupportedExtensions.Contains(extension) &&
               (extension.Length > 0 || !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal));
    }

    private static string GetImageMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static void ValidateImage(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("图片不存在。", path);
        if (info.Length <= 0) throw new InvalidDataException("图片文件为空。");
        if (info.Length > MaxImageFileBytes) throw new InvalidDataException("单张图片不能超过 20 MB。");
        if (!IsImageExtension(path)) throw new InvalidDataException("仅支持 PNG、JPEG 和 WebP 图片。");

        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < 12) throw new InvalidDataException("图片文件头无效。");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var valid = extension switch
        {
            ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".webp" => header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new InvalidDataException("图片扩展名与实际文件格式不一致。");
    }

    private static ImageDescriptionResult BuildMetadataDescription(string fileName, string sourceRelativePath)
    {
        var text = $"{Path.GetDirectoryName(sourceRelativePath)} {Path.GetFileNameWithoutExtension(fileName)}"
            .Replace(Path.DirectorySeparatorChar, ' ')
            .Replace(Path.AltDirectorySeparatorChar, ' ')
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        var tags = string.Join(',', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12));
        return new ImageDescriptionResult
        {
            Description = text.Length == 0 ? "未命名图片" : text,
            Tags = tags,
            Source = ImageDescriptionSource.MetadataFallback
        };
    }

    private static string BuildImageSemanticContent(
        string fileName,
        string sourceRelativePath,
        string description,
        string tags) =>
        $"图片标题：{Path.GetFileNameWithoutExtension(fileName)}\n" +
        $"来源目录：{sourceRelativePath}\n" +
        $"画面描述：{description}\n" +
        $"标签：{tags}";

    private static string NormalizeTags(string tags) => string.Join(',',
        (tags ?? string.Empty).Split(new[] { ',', '，', ';', '；', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20));

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return string.Join('/', path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment is not "." and not ".."));
    }

    private static long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string FormatImportError(string itemName, Exception exception)
    {
        var message = exception is HttpOperationException operation &&
                      !string.IsNullOrWhiteSpace(operation.ResponseContent)
            ? operation.ResponseContent
            : exception.GetBaseException().Message;
        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (message.Contains("batch size", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("larger than 10", StringComparison.OrdinalIgnoreCase))
            message = "Embedding 服务单次最多接收 10 个文本分块";
        if (message.Length > 320) message = message[..320] + "…";
        return $"{itemName}：{message}";
    }

    private static void TryDeleteFile(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
