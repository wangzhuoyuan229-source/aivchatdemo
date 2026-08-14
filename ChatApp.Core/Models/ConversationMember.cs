namespace ChatApp.Core.Models;

/// <summary>
/// Membership link between a group conversation and one AI role.
/// A group conversation has one row per member; <see cref="DisplayOrder"/>
/// drives round-robin speaking order.
/// </summary>
public class ConversationMember
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    public int RoleId { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Speaking/turn order inside the group (0-based).</summary>
    public int DisplayOrder { get; set; }
}
