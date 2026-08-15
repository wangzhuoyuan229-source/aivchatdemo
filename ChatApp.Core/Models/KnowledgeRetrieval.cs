namespace ChatApp.Core.Models;

public enum KnowledgeRetrievalStatus
{
    Disabled = 0,
    Found = 1,
    NoRelevantMatch = 2,
    Unavailable = 3
}

/// <summary>Inputs for a role-scoped knowledge lookup.</summary>
public sealed class KnowledgeRetrievalRequest
{
    public string Query { get; init; } = string.Empty;

    public IReadOnlyCollection<int> AllowedGroupIds { get; init; } = Array.Empty<int>();

    public int TopK { get; init; } = 5;

    public double MinScore { get; init; } = 0.35;

    public int ContextCharBudget { get; init; } = 6000;

    public int NeighborRadius { get; init; } = 1;
}

/// <summary>A source-aware knowledge chunk included in the grounded context.</summary>
public sealed class KnowledgeHit
{
    public int DocumentId { get; init; }

    public string DocumentTitle { get; init; } = string.Empty;

    public int ChunkIndex { get; init; }

    public string Content { get; init; } = string.Empty;

    public double Score { get; init; }

    public bool IsDirectMatch { get; init; }
}

/// <summary>
/// Separates a successful lookup from an empty lookup and an unavailable
/// retrieval service so orchestration never silently falls back to invention.
/// </summary>
public sealed class KnowledgeRetrievalResult
{
    public KnowledgeRetrievalStatus Status { get; init; }

    public IReadOnlyList<KnowledgeHit> Hits { get; init; } = Array.Empty<KnowledgeHit>();

    public string? Detail { get; init; }

    public static KnowledgeRetrievalResult Disabled(string? detail = null) => new()
    {
        Status = KnowledgeRetrievalStatus.Disabled,
        Detail = detail
    };

    public static KnowledgeRetrievalResult NoRelevantMatch(string? detail = null) => new()
    {
        Status = KnowledgeRetrievalStatus.NoRelevantMatch,
        Detail = detail
    };

    public static KnowledgeRetrievalResult Unavailable(string? detail = null) => new()
    {
        Status = KnowledgeRetrievalStatus.Unavailable,
        Detail = detail
    };
}
