namespace ChatApp.Core.Models;

/// <summary>
/// A persisted long-term memory fragment derived from past conversation.
/// The actual embedding vector is stored in the vector store, keyed by <see cref="ExternalId"/>.
/// </summary>
public class MemoryEntry
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public int? ConversationId { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Identifier used as the vector-store key, e.g. "mem:{roleId}:{entryId}".</summary>
    public string ExternalId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A record stored in the vector store (embedding + content + metadata).</summary>
public class VectorRecord
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Namespace, e.g. "memory:3" or "knowledge:5".</summary>
    public string Scope { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public float[] Embedding { get; set; } = Array.Empty<float>();

    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>A single search hit returned by the vector store.</summary>
public class VectorSearchHit
{
    public VectorRecord Record { get; set; } = new();
    public double Score { get; set; }
}
