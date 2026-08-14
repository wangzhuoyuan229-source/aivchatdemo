using ChatApp.Core.Models;

namespace ChatApp.Core.Services;

/// <summary>Lightweight vector store abstraction (F4 / F5).</summary>
public interface IVectorStore
{
    Task UpsertAsync(VectorRecord record, CancellationToken ct = default);

    Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, string scope, int topK, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task DeleteByScopeAsync(string scope, CancellationToken ct = default);
}

/// <summary>Generates embeddings via an OpenAI-compatible endpoint.</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
