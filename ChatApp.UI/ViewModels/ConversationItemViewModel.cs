using ChatApp.Core.Models;

namespace ChatApp.UI.ViewModels;

/// <summary>A reusable conversation entry used by the recent group-chat list.</summary>
public class ConversationItemViewModel : ViewModelBase
{
    public Conversation Conversation { get; init; } = new();
    public string RoleName { get; init; } = string.Empty;
    public string Avatar { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberAvatars { get; init; } = Array.Empty<string>();
    public bool IsGroup { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Conversation.Title) ? RoleName : Conversation.Title;
    public bool IsPinned => Conversation.IsPinned;
    public string PinIcon => Conversation.IsPinned ? "📌" : "";
}
