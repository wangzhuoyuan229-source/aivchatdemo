namespace ChatApp.Core.Models;

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

    /// <summary>Optional group id. Null means "ungrouped".</summary>
    public int? GroupId { get; set; }

    /// <summary>Scope used in the vector store, e.g. "knowledge:{docId}".</summary>
    public string Scope => $"knowledge:{Id}";

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A text chunk of a knowledge document (metadata only; embedding lives in vector store).</summary>
public class KnowledgeChunk
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;
}
