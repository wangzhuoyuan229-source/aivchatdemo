using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Long-term memory: derives memory fragments from conversation history,
/// embeds them and recalls the most relevant ones via the vector store (F4).
/// </summary>
public interface IMemoryService
{
    /// <summary>Persists and embeds a single memory fragment for a role.</summary>
    Task RememberAsync(int roleId, int? conversationId, string content, CancellationToken ct = default);

    /// <summary>Processes the latest messages of a conversation, embedding new memories if the batch threshold is reached.</summary>
    Task ProcessConversationAsync(int conversationId, CancellationToken ct = default);

    /// <summary>Recalls the Top-K most relevant memory fragments for the given query text.</summary>
    Task<IReadOnlyList<VectorSearchHit>> RecallAsync(int roleId, string query, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryEntry>> ListAsync(int roleId, CancellationToken ct = default);

    /// <summary>Updates a memory fragment's content and re-embeds its vector.</summary>
    Task UpdateAsync(int memoryId, string content, CancellationToken ct = default);

    Task ForgetAsync(int memoryId, CancellationToken ct = default);

    Task ClearForRoleAsync(int roleId, CancellationToken ct = default);
}
