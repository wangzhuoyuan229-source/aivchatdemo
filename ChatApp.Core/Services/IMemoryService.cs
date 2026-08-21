using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Long-term memory: derives memory fragments from conversation history,
/// embeds them and recalls the most relevant ones via the vector store (F4).
/// </summary>
public interface IMemoryService
{
    /// <summary>Persists a shared memory and records which role triggered it.</summary>
    Task RememberAsync(int sourceRoleId, int? conversationId, string content, CancellationToken ct = default);

    /// <summary>Processes the latest messages of a conversation, embedding new memories if the batch threshold is reached.</summary>
    Task ProcessConversationAsync(int conversationId, CancellationToken ct = default);

    /// <summary>Recalls the Top-K most relevant fragments from the shared memory pool.</summary>
    Task<IReadOnlyList<VectorSearchHit>> RecallSharedAsync(string query, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryEntry>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Updates a memory fragment's content and re-embeds its vector.</summary>
    Task UpdateAsync(int memoryId, string content, CancellationToken ct = default);

    Task ForgetAsync(int memoryId, CancellationToken ct = default);

    Task ClearAllAsync(CancellationToken ct = default);
}
