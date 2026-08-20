using CommunityToolkit.Mvvm.ComponentModel;
using ChatApp.Core.Models;
using System.Collections.ObjectModel;

namespace ChatApp.UI.ViewModels;

/// <summary>A knowledge citation tag rendered under an assistant reply.</summary>
public class ChatCitationViewModel
{
    public int DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string DisplayText => $"📄 {(string.IsNullOrWhiteSpace(Title) ? $"文档 #{DocumentId}" : Title)}";
}

/// <summary>A single message bubble shown in the chat (display wrapper around a message).</summary>
public partial class ChatBubbleViewModel : ViewModelBase
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private bool _canRegenerate;
    [ObservableProperty] private bool _canEdit;

    public int Id { get; set; }
    public MessageAuthor Author { get; init; }
    public bool IsUser => Author == MessageAuthor.User;
    public bool IsAssistant => Author == MessageAuthor.Assistant;
    public string Avatar { get; init; } = "🤖";
    public string RoleName { get; init; } = string.Empty;
    /// <summary>True for bubbles in a group chat — shows the speaker name label.</summary>
    public bool IsGroupBubble { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string TimeText => CreatedAt.ToLocalTime().ToString("HH:mm");

    public ObservableCollection<ChatAttachmentViewModel> Attachments { get; } = new();

    public ObservableCollection<ChatCitationViewModel> Citations { get; } = new();

    /// <summary>True when at least one citation tag exists (drives visibility in XAML).</summary>
    public bool HasCitations => Citations.Count > 0;

    public ChatBubbleViewModel()
    {
        Citations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCitations));
    }

    public void SetAttachments(IEnumerable<MessageAttachment> attachments)
    {
        Attachments.Clear();
        foreach (var attachment in attachments.Where(a => a.Kind == MessageAttachmentKind.Image))
            Attachments.Add(new ChatAttachmentViewModel(attachment));
    }
}
