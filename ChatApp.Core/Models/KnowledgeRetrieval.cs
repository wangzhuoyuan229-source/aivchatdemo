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

    /// <summary>True when the current topic asks about the role's visible appearance.</summary>
    public bool AppearanceFocused { get; init; }

    public IReadOnlyCollection<int> AllowedGroupIds { get; init; } = Array.Empty<int>();

    public int TopK { get; init; } = 5;

    public double MinScore { get; init; } = 0.35;

    public int ContextCharBudget { get; init; } = 6000;

    public int NeighborRadius { get; init; } = 1;

    public int ImageTopK { get; init; } = 5;

    public double ImageMinScore { get; init; } = 0.35;
}

/// <summary>A managed knowledge image that may be attached to a grounded reply.</summary>
public sealed class KnowledgeImageHit
{
    public int DocumentId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string SourceRelativePath { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    public string StorageKey { get; init; } = string.Empty;

    public string MimeType { get; init; } = string.Empty;

    public double Score { get; init; }
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

    public IReadOnlyList<KnowledgeImageHit> ImageHits { get; init; } = Array.Empty<KnowledgeImageHit>();

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

public enum KnowledgeImportStage
{
    Scanning = 0,
    Copying = 1,
    Describing = 2,
    Embedding = 3,
    Persisting = 4,
    Completed = 5
}

public sealed class KnowledgeImportProgress
{
    public KnowledgeImportStage Stage { get; init; }

    public int Completed { get; init; }

    public int Total { get; init; }

    public int Succeeded { get; init; }

    public int Failed { get; init; }

    public int FallbackCount { get; init; }

    public int SkippedCount { get; init; }

    public long ProcessedBytes { get; init; }

    public long TotalBytes { get; init; }

    public string CurrentFile { get; init; } = string.Empty;

    /// <summary>Most recent per-item failure, safe for display and never containing API keys.</summary>
    public string LastError { get; init; } = string.Empty;
}
