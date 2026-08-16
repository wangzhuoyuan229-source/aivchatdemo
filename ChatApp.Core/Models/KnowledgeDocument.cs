namespace ChatApp.Core.Models;

public enum KnowledgeItemKind
{
    TextDocument = 0,
    Image = 1
}

public enum ImageDescriptionSource
{
    None = 0,
    VisionModel = 1,
    MetadataFallback = 2,
    Manual = 3
}

/// <summary>An imported knowledge-base document (novel, setting bible, etc.).</summary>
public class KnowledgeDocument
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>File extension without dot, e.g. "txt", "md", "pdf".</summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Total character count of the source document.</summary>
    public long CharCount { get; set; }

    public int ChunkCount { get; set; }

    /// <summary>Text document or managed image asset.</summary>
    public KnowledgeItemKind Kind { get; set; } = KnowledgeItemKind.TextDocument;

    /// <summary>Relative key below the managed knowledge directory. Empty for legacy text documents.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    /// <summary>Searchable description generated for an image.</summary>
    public string SemanticDescription { get; set; } = string.Empty;

    /// <summary>Comma-separated searchable image tags.</summary>
    public string Tags { get; set; } = string.Empty;

    public ImageDescriptionSource DescriptionSource { get; set; }

    public string DescriptionProvider { get; set; } = string.Empty;

    public string DescriptionModel { get; set; } = string.Empty;

    /// <summary>Relative path at import time; used as additional retrieval metadata.</summary>
    public string SourceRelativePath { get; set; } = string.Empty;

    /// <summary>Optional group id. Null means "ungrouped".</summary>
    public int? GroupId { get; set; }

    /// <summary>Stable document scope retained for backward-compatible local storage.</summary>
    public string Scope => $"knowledge:{Id}";

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A locally searchable text chunk of a knowledge document.</summary>
public class KnowledgeChunk
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;
}
