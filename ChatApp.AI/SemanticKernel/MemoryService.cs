using System.Text;
using ChatApp.AI.Caching;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatApp.AI.SemanticKernel;

/// <summary>Long-term memory: batches conversation history, embeds fragments and recalls them (F4).</summary>
public class MemoryService : IMemoryService
{
    private const string SharedScope = "memory:shared";
    private const string SourceRoleIdKey = "sourceRoleId";
    private const string SourceRoleNameKey = "sourceRoleName";
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectors;
    private readonly IChatHistoryService _history;
    private readonly IConfigurationService _config;
    private readonly ILogger<MemoryService> _logger;

    /// <summary>Shared recall dedup so repeated similar questions skip embedding + search (3.3).</summary>
    private readonly ScopedQueryCache<IReadOnlyList<VectorSearchHit>> _recallCache = new();

    public MemoryService(
        IDbContextFactory<AppDbContext> factory,
        IEmbeddingService embedding,
        IVectorStore vectors,
        IChatHistoryService history,
        IConfigurationService config,
        ILogger<MemoryService> logger)
    {
        _factory = factory;
        _embedding = embedding;
        _vectors = vectors;
        _history = history;
        _config = config;
        _logger = logger;
    }

    public async Task RememberAsync(int sourceRoleId, int? conversationId, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var vector = await _embedding.EmbedAsync(content, ct);
        var externalId = $"mem:{sourceRoleId}:{Guid.NewGuid():N}";
        await _vectors.UpsertAsync(new VectorRecord
        {
            Id = externalId,
            Scope = SharedScope,
            Content = content,
            Embedding = vector,
            Metadata = new Dictionary<string, string> { [SourceRoleIdKey] = sourceRoleId.ToString() }
        }, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MemoryEntries.Add(new MemoryEntry
        {
            RoleId = sourceRoleId,
            ConversationId = conversationId,
            Content = content,
            ExternalId = externalId
        });
        await db.SaveChangesAsync(ct);
        _recallCache.InvalidateScope(SharedScope);
    }

    public async Task ProcessConversationAsync(int conversationId, CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        var conv = await _history.GetConversationAsync(conversationId, ct);
        if (conv is null) return;

        if (conv.Type == ConversationType.Private && conv.RoleId is null) return;
        var progressKey = $"memconv:{conversationId}";
        var lastId = await ReadProgressAsync(progressKey, ct);

        var newer = await _history.GetMessagesAsync(conversationId, int.MaxValue, ct);
        var pending = newer.Where(m => m.Id > lastId).ToList();
        if (pending.Count < settings.MemoryBatchSize) return;

        // Take exactly one batch.
        var batch = pending.Take(settings.MemoryBatchSize).ToList();
        var sourceBatches = conv.Type == ConversationType.Private
            ? new[] { (RoleId: conv.RoleId!.Value, Messages: (IReadOnlyList<Message>)batch) }
            : batch
                .Where(message => message.Author == MessageAuthor.Assistant && message.RoleId > 0)
                .Select(message => message.RoleId)
                .Distinct()
                .Select(roleId => (
                    RoleId: roleId,
                    Messages: (IReadOnlyList<Message>)batch
                        .Where(message => message.Author == MessageAuthor.User || message.RoleId == roleId)
                        .ToList()))
                .ToArray();

        var storedCount = 0;
        foreach (var sourceBatch in sourceBatches)
        {
            var fragments = ChunkForEmbedding(FormatBatch(sourceBatch.Messages));
            if (fragments.Count == 0) continue;
            var vectors = await _embedding.EmbedBatchAsync(fragments, ct);
            for (int i = 0; i < fragments.Count; i++)
            {
                var externalId = $"mem:{sourceBatch.RoleId}:{conversationId}:{batch[0].Id}:{i}";
                await _vectors.UpsertAsync(new VectorRecord
                {
                    Id = externalId,
                    Scope = SharedScope,
                    Content = fragments[i],
                    Embedding = vectors[i],
                    Metadata = new Dictionary<string, string>
                    {
                        [SourceRoleIdKey] = sourceBatch.RoleId.ToString()
                    }
                }, ct);

                await using var db = await _factory.CreateDbContextAsync(ct);
                db.MemoryEntries.Add(new MemoryEntry
                {
                    RoleId = sourceBatch.RoleId,
                    ConversationId = conversationId,
                    Content = fragments[i],
                    ExternalId = externalId
                });
                await db.SaveChangesAsync(ct);
                storedCount++;
            }
        }

        if (storedCount == 0) return;

        await WriteProgressAsync(progressKey, batch[^1].Id, ct);
        _recallCache.InvalidateScope(SharedScope);
        _logger.LogInformation("Embedded {Count} shared memory fragments for conversation {Id}.", storedCount, conversationId);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> RecallSharedAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<VectorSearchHit>();
        var key = NormalizeQuery(query);
        if (_recallCache.TryGet(SharedScope, key, out var cached))
            return await AddSourceRoleMetadataAsync(cached, ct);

        var settings = await _config.LoadAsync(ct);
        var qv = await _embedding.EmbedAsync(query, ct);
        var hits = await _vectors.SearchAsync(
            qv,
            SharedScope,
            settings.MemoryTopK,
            minScore: 0,
            allowedIds: null,
            ct: ct);
        var enrichedHits = await AddSourceRoleMetadataAsync(hits, ct);
        _recallCache.Set(SharedScope, key, enrichedHits);
        return enrichedHits;
    }

    private async Task<IReadOnlyList<VectorSearchHit>> AddSourceRoleMetadataAsync(
        IReadOnlyList<VectorSearchHit> hits,
        CancellationToken ct)
    {
        var externalIds = hits.Select(hit => hit.Record.Id).Distinct(StringComparer.Ordinal).ToList();
        if (externalIds.Count == 0) return Array.Empty<VectorSearchHit>();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var entries = await db.MemoryEntries.AsNoTracking()
            .Where(entry => externalIds.Contains(entry.ExternalId))
            .ToListAsync(ct);
        var roleIds = entries.Select(entry => entry.RoleId).Distinct().ToList();
        var roleNames = await db.Roles.AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name, ct);
        var entriesByExternalId = entries.ToDictionary(entry => entry.ExternalId, StringComparer.Ordinal);

        var result = new List<VectorSearchHit>(hits.Count);
        foreach (var hit in hits)
        {
            if (!entriesByExternalId.TryGetValue(hit.Record.Id, out var entry)) continue;
            hit.Record.Metadata[SourceRoleIdKey] = entry.RoleId.ToString();
            hit.Record.Metadata[SourceRoleNameKey] = roleNames.GetValueOrDefault(entry.RoleId, $"角色 #{entry.RoleId}");
            result.Add(hit);
        }
        return result;
    }

    /// <summary>Lowercases and collapses whitespace so similar phrasings share a cache key.</summary>
    private static string NormalizeQuery(string query) =>
        string.Join(" ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public async Task<IReadOnlyList<MemoryEntry>> ListAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MemoryEntries.AsNoTracking().OrderByDescending(m => m.Id).ToListAsync(ct);
    }

    public async Task UpdateAsync(int memoryId, string content, CancellationToken ct = default)
    {
        var normalized = content.Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("记忆内容不能为空。");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entry = await db.MemoryEntries.FirstOrDefaultAsync(m => m.Id == memoryId, ct)
            ?? throw new InvalidOperationException("记忆条目不存在。");
        entry.Content = normalized;
        await db.SaveChangesAsync(ct);

        // Re-embed under the same vector key so recall reflects the edit immediately.
        try
        {
            var vector = await _embedding.EmbedAsync(normalized, ct);
            await _vectors.UpsertAsync(new VectorRecord
            {
                Id = entry.ExternalId,
                Scope = SharedScope,
                Content = normalized,
                Embedding = vector,
                Metadata = new Dictionary<string, string> { [SourceRoleIdKey] = entry.RoleId.ToString() }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Re-embedding edited memory {Id} failed; content update kept.", memoryId);
        }
        _recallCache.InvalidateScope(SharedScope);
    }

    public async Task ForgetAsync(int memoryId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entry = await db.MemoryEntries.FirstOrDefaultAsync(m => m.Id == memoryId, ct);
        if (entry is null) return;
        if (!string.IsNullOrEmpty(entry.ExternalId))
            await _vectors.DeleteAsync(entry.ExternalId, ct);
        db.MemoryEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        _recallCache.InvalidateScope(SharedScope);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _vectors.DeleteByScopeAsync(SharedScope, ct);
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MemoryEntries.RemoveRange(db.MemoryEntries);
        await db.SaveChangesAsync(ct);
        _recallCache.InvalidateScope(SharedScope);
    }

    private async Task<int> ReadProgressAsync(string key, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return row is not null && int.TryParse(row.Value, out var v) ? v : 0;
    }

    private async Task WriteProgressAsync(string key, int value, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = key, Value = value.ToString() });
        else
            row.Value = value.ToString();
        await db.SaveChangesAsync(ct);
    }

    private static string FormatBatch(IReadOnlyList<Message> batch)
    {
        var sb = new StringBuilder();
        foreach (var m in batch)
        {
            sb.Append(m.Author == MessageAuthor.User ? "用户：" : "AI：");
            sb.AppendLine(m.Content);
        }
        return sb.ToString();
    }

    private static IReadOnlyList<string> ChunkForEmbedding(string text, int target = 1000)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        for (int i = 0; i < text.Length; i += target)
            result.Add(text.Substring(i, Math.Min(target, text.Length - i)));
        return result;
    }
}
