namespace ChatApp.Core.Models;

/// <summary>
/// An AI character with persona, speaking style and background.
/// </summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Avatar: an emoji glyph, a base64 data URI, or a local file path.</summary>
    public string Avatar { get; set; } = string.Empty;

    /// <summary>Free-form description shown in the role list.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Background / world setting injected into the system prompt.</summary>
    public string Background { get; set; } = string.Empty;

    /// <summary>Personality traits injected into the system prompt.</summary>
    public string Personality { get; set; } = string.Empty;

    /// <summary>Speaking style description injected into the system prompt.</summary>
    public string SpeakingStyle { get; set; } = string.Empty;

    /// <summary>Full assembled system prompt. When empty, it is built from the fields above.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Greeting message sent when a conversation starts.</summary>
    public string Greeting { get; set; } = string.Empty;

    /// <summary>P2: affinity score (0-100), default 50.</summary>
    public int Affinity { get; set; } = 50;

    /// <summary>True for factory-seeded roles, false for user-created roles.</summary>
    public bool IsPreset { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
