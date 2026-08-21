namespace ChatApp.Core.Models;

/// <summary>Author of a chat message.</summary>
public enum MessageAuthor
{
    User = 0,
    Assistant = 1,
    System = 2
}

/// <summary>A single chat message inside a conversation.</summary>
public class Message
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    public int RoleId { get; set; }

    public MessageAuthor Author { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated knowledge-document ids whose content grounded this reply
    /// (citation tracing). Empty when the reply used no knowledge hits.
    /// </summary>
    public string CitedDocumentIds { get; set; } = string.Empty;

    /// <summary>Token/character count for the message (for context-window accounting).</summary>
    public int TokenEstimate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MessageAttachment> Attachments { get; set; } = new();

    /// <summary>Parses <see cref="CitedDocumentIds"/> into a distinct, ordered id list.</summary>
    public IReadOnlyList<int> GetCitedDocumentIdList() => MessageCitations.Parse(CitedDocumentIds);
}

/// <summary>Helpers for the comma-separated citation column on <see cref="Message"/>.</summary>
public static class MessageCitations
{
    public static IReadOnlyList<int> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<int>();
        var result = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0 && !result.Contains(id))
                result.Add(id);
        }
        return result;
    }

    public static string Format(IEnumerable<int> documentIds) =>
        string.Join(",", documentIds.Distinct());
}

public enum MessageAttachmentKind
{
    Image = 0
}

/// <summary>An immutable snapshot attached to a persisted chat message.</summary>
public class MessageAttachment
{
    public int Id { get; set; }

    public int MessageId { get; set; }

    public MessageAttachmentKind Kind { get; set; } = MessageAttachmentKind.Image;

    /// <summary>Relative key below the managed chat-attachment directory.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public int? SourceKnowledgeDocumentId { get; set; }

    public Message? Message { get; set; }
}

/// <summary>
/// A conversation session. For <see cref="ConversationType.Private"/> it is bound
/// to a single role via <see cref="RoleId"/>; for <see cref="ConversationType.Group"/>
/// <see cref="RoleId"/> is null and members live in <c>ConversationMember</c>.
/// </summary>
public class Conversation
{
    public int Id { get; set; }

    /// <summary>Bound role for private chats; null for group chats.</summary>
    public int? RoleId { get; set; }

    public ConversationType Type { get; set; } = ConversationType.Private;

    public string Title { get; set; } = string.Empty;

    /// <summary>Optional custom avatar for group chats; empty uses a member-avatar collage.</summary>
    public string Avatar { get; set; } = string.Empty;

    /// <summary>Pinned conversations sort above unpinned ones regardless of recency.</summary>
    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
