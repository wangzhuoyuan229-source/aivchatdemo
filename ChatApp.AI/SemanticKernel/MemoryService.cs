using System.Text;
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
    private const string ScopePrefix = "memory:";
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectors;
    private readonly IChatHistoryService _history;
    private readonly IConfigurationService _config;
    private readonly ILogger<MemoryService> _logger;

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

    public async Task RememberAsync(int roleId, int? conversationId, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var vector = await _embedding.EmbedAsync(content, ct);
        var externalId = $"mem:{roleId}:{Guid.NewGuid():N}";
        await _vectors.UpsertAsync(new VectorRecord
        {
            Id = externalId,
            Scope = $"{ScopePrefix}{roleId}",
            Content = content,
            Embedding = vector
        }, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MemoryEntries.Add(new MemoryEntry
        {
            RoleId = roleId,
            ConversationId = conversationId,
            Content = content,
            ExternalId = externalId
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task ProcessConversationAsync(int conversationId, CancellationToken ct = default)
    {
        var settings = await _config.LoadAsync(ct);
        var conv = await _history.GetConversationAsync(conversationId, ct);
        if (conv is null) return;

        // Phase 1: group-chat write-side memory is skipped (members each have their own
        // scope; ProcessConversationAsync attributes memory to a single role). Private
        // chats only — group chats rely on RecallAsync reading each member's existing memory.
        if (conv.Type != ConversationType.Private || conv.RoleId is null) return;

        var roleId = conv.RoleId.Value;
        var progressKey = $"memconv:{conversationId}";
        var lastId = await ReadProgressAsync(progressKey, ct);

        var newer = await _history.GetMessagesAsync(conversationId, int.MaxValue, ct);
        var pending = newer.Where(m => m.Id > lastId).ToList();
        if (pending.Count < settings.MemoryBatchSize) return;

        // Take exactly one batch.
        var batch = pending.Take(settings.MemoryBatchSize).ToList();
        var text = FormatBatch(batch);
        var fragments = ChunkForEmbedding(text);
        if (fragments.Count == 0) return;

        var vectors = await _embedding.EmbedBatchAsync(fragments, ct);
        for (int i = 0; i < fragments.Count; i++)
        {
            var externalId = $"mem:{roleId}:{conversationId}:{batch[0].Id}:{i}";
            await _vectors.UpsertAsync(new VectorRecord
            {
                Id = externalId,
                Scope = $"{ScopePrefix}{roleId}",
                Content = fragments[i],
                Embedding = vectors[i]
            }, ct);

            await using var db = await _factory.CreateDbContextAsync(ct);
            db.MemoryEntries.Add(new MemoryEntry
            {
                RoleId = roleId,
                ConversationId = conversationId,
                Content = fragments[i],
                ExternalId = externalId
            });
            await db.SaveChangesAsync(ct);
        }

        await WriteProgressAsync(progressKey, batch[^1].Id, ct);
        _logger.LogInformation("Embedded {Count} memory fragments for conversation {Id}.", fragments.Count, conversationId);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> RecallAsync(int roleId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<VectorSearchHit>();
        var settings = await _config.LoadAsync(ct);
        var qv = await _embedding.EmbedAsync(query, ct);
        return await _vectors.SearchAsync(qv, $"{ScopePrefix}{roleId}", settings.MemoryTopK, ct);
    }

    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(int roleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MemoryEntries.AsNoTracking().Where(m => m.RoleId == roleId).OrderByDescending(m => m.Id).ToListAsync(ct);
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
    }

    public async Task ClearForRoleAsync(int roleId, CancellationToken ct = default)
    {
        await _vectors.DeleteByScopeAsync($"{ScopePrefix}{roleId}", ct);
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MemoryEntries.RemoveRange(db.MemoryEntries.Where(m => m.RoleId == roleId));
        await db.SaveChangesAsync(ct);
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
