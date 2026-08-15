using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private readonly IChatService _chat;
    private readonly IGroupChatService _groupChat;
    private readonly IChatHistoryService _history;
    private readonly IRoleService _roles;
    private readonly ILogger<ChatViewModel> _logger;
    private CancellationTokenSource? _cts;

    /// <summary>Member roles keyed by RoleId (group mode only).</summary>
    private readonly Dictionary<int, Role> _groupMembers = new();
    /// <summary>Streaming bubbles keyed by RoleId (group mode only).</summary>
    private readonly Dictionary<int, ChatBubbleViewModel> _activeBubbles = new();

    [ObservableProperty] private Conversation? _conversation;
    [ObservableProperty] private Role? _role;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isGroupMode;
    [ObservableProperty] private string _conversationSubtitle = string.Empty;

    public ObservableCollection<ChatBubbleViewModel> Messages { get; } = new();

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(InputText) && Conversation is not null;

    public ChatViewModel(IChatService chat, IGroupChatService groupChat, IChatHistoryService history, IRoleService roles, ILogger<ChatViewModel> logger)
    {
        _chat = chat;
        _groupChat = groupChat;
        _history = history;
        _roles = roles;
        _logger = logger;
    }

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnConversationChanged(Conversation? value) => SendCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync(int conversationId)
    {
        var conv = await _history.GetConversationAsync(conversationId);
        if (conv is null) return;
        Conversation = conv;
        IsGroupMode = false;
        _groupMembers.Clear();
        _activeBubbles.Clear();
        Role = await _roles.GetAsync(conv.RoleId ?? 0);
        Title = Role is null ? conv.Title : $"{Role.Avatar}  {Role.Name}";
        ConversationSubtitle = Role?.Description ?? string.Empty;
        Messages.Clear();
        var msgs = await _history.GetMessagesAsync(conversationId);
        foreach (var m in msgs) Messages.Add(ToBubble(m));
    }

    /// <summary>Loads a group conversation: resolves members and renders history with per-speaker avatars.</summary>
    public async Task LoadGroupAsync(int conversationId)
    {
        var conv = await _history.GetConversationAsync(conversationId);
        if (conv is null) return;
        Conversation = conv;
        IsGroupMode = true;
        Role = null;
        _groupMembers.Clear();
        _activeBubbles.Clear();

        var members = await _history.GetMembersAsync(conversationId);
        foreach (var m in members)
        {
            var r = await _roles.GetAsync(m.RoleId);
            if (r is not null) _groupMembers[m.RoleId] = r;
        }

        Title = string.IsNullOrWhiteSpace(conv.Title)
            ? string.Join("、", _groupMembers.Values.Select(r => r.Name))
            : conv.Title;
        ConversationSubtitle = $"{_groupMembers.Count} 位成员 · {string.Join("、", _groupMembers.Values.Select(r => r.Name))}";

        Messages.Clear();
        var msgs = await _history.GetMessagesAsync(conversationId);
        foreach (var m in msgs) Messages.Add(ToBubble(m));
    }

    /// <summary>清空当前对话状态（删除当前会话或退出对话时调用）。</summary>
    public void ClearConversation()
    {
        Conversation = null;
        Role = null;
        IsGroupMode = false;
        _groupMembers.Clear();
        _activeBubbles.Clear();
        Messages.Clear();
        Title = "对话";
        ConversationSubtitle = string.Empty;
        StatusText = string.Empty;
        InputText = string.Empty;
    }

    /// <summary>
    /// 刷新当前会话的消息显示（用于从其他页面切回时强制重新加载并触发 UI 更新）。
    /// 如果当前没有会话，不做任何事。
    /// </summary>
    public async Task RefreshCurrentAsync()
    {
        if (Conversation is null) return;
        var convId = Conversation.Id;
        var conv = await _history.GetConversationAsync(convId);
        if (conv is null)
        {
            // 会话已被删除
            ClearConversation();
            return;
        }
        if (conv.Type == ConversationType.Group)
            await LoadGroupAsync(convId);
        else
            await LoadAsync(convId);
    }

    [ObservableProperty] private string _title = "对话";

    public async Task StartForRoleAsync(Role role)
    {
        var conv = await _history.CreateConversationAsync(role.Id, $"与{role.Name}的对话");
        Conversation = conv;
        Role = role;
        IsGroupMode = false;
        Title = $"{role.Avatar}  {role.Name}";
        ConversationSubtitle = role.Description;
        Messages.Clear();

        try
        {
            var greeting = await _chat.GreetAsync(conv.Id);
            Messages.Add(ToBubble(greeting));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Greeting failed.");
            Messages.Add(new ChatBubbleViewModel { Author = MessageAuthor.Assistant, Content = $"（无法生成问候语：{ex.Message}）", Avatar = role.Avatar, RoleName = role.Name });
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (Conversation is null) return;
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
        SendCommand.NotifyCanExecuteChanged();

        if (IsGroupMode)
        {
            await SendGroupAsync(text);
            return;
        }

        if (Role is null) return;

        var userBubble = new ChatBubbleViewModel { Author = MessageAuthor.User, Content = text, Avatar = "🧑", RoleName = "我" };
        Messages.Add(userBubble);

        var assistantBubble = new ChatBubbleViewModel
        {
            Author = MessageAuthor.Assistant,
            Content = string.Empty,
            Avatar = Role.Avatar,
            RoleName = Role.Name,
            IsStreaming = true
        };
        Messages.Add(assistantBubble);

        IsSending = true;
        StatusText = "正在思考...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(delta => assistantBubble.Content += delta);

        try
        {
            var final = await _chat.SendAsync(Conversation.Id, text, progress, _cts.Token);
            assistantBubble.Id = final.Id;
            assistantBubble.Content = final.Content;
        }
        catch (OperationCanceledException)
        {
            assistantBubble.Content += "\n\n[已停止]";
        }
        catch (Exception ex)
        {
            assistantBubble.Content = $"⚠️ {ex.Message}";
            _logger.LogError(ex, "Send failed.");
        }
        finally
        {
            assistantBubble.IsStreaming = false;
            IsSending = false;
            StatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Group-chat send: one user bubble, then N streaming speaker bubbles driven by events.</summary>
    private async Task SendGroupAsync(string text)
    {
        var userBubble = new ChatBubbleViewModel { Author = MessageAuthor.User, Content = text, Avatar = "🧑", RoleName = "我" };
        Messages.Add(userBubble);

        IsSending = true;
        StatusText = "群聊发言中...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<GroupChatEvent>(OnGroupEvent);

        try
        {
            await _groupChat.SendAsync(Conversation!.Id, text, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Mark any still-streaming bubble as stopped.
            foreach (var b in _activeBubbles.Values)
            {
                b.Content += "\n\n[已停止]";
                b.IsStreaming = false;
            }
            _activeBubbles.Clear();
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatBubbleViewModel { Author = MessageAuthor.Assistant, Content = $"⚠️ {ex.Message}", Avatar = "⚠️", RoleName = "错误" });
            _logger.LogError(ex, "Group send failed.");
        }
        finally
        {
            // Safety net: ensure no bubble is left streaming if events were missed.
            foreach (var b in _activeBubbles.Values) b.IsStreaming = false;
            _activeBubbles.Clear();
            IsSending = false;
            StatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Maps a <see cref="GroupChatEvent"/> to bubble mutations on the UI thread.</summary>
    private void OnGroupEvent(GroupChatEvent ev)
    {
        switch (ev)
        {
            case SpeakerStarted started:
                if (_groupMembers.TryGetValue(started.RoleId, out var r))
                {
                    var bubble = new ChatBubbleViewModel
                    {
                        Author = MessageAuthor.Assistant,
                        Content = string.Empty,
                        Avatar = r.Avatar,
                        RoleName = r.Name,
                        IsGroupBubble = true,
                        IsStreaming = true
                    };
                    Messages.Add(bubble);
                    _activeBubbles[started.RoleId] = bubble;
                }
                break;

            case SpeakerDelta delta:
                if (_activeBubbles.TryGetValue(delta.RoleId, out var b))
                    b.Content += delta.Delta;
                break;

            case SpeakerFinished finished:
                if (_activeBubbles.TryGetValue(finished.RoleId, out var fb))
                {
                    fb.Id = finished.FinalMessage.Id;
                    fb.Content = finished.FinalMessage.Content;
                    fb.IsStreaming = false;
                    _activeBubbles.Remove(finished.RoleId);
                }
                break;

            case TurnFinished:
                // IsSending/StatusText are reset in SendGroupAsync's finally.
                break;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
    }

    private ChatBubbleViewModel ToBubble(Message m)
    {
        // Group mode: resolve avatar/name per message via member map.
        if (IsGroupMode)
        {
            var gRole = m.Author == MessageAuthor.User ? null : _groupMembers.GetValueOrDefault(m.RoleId);
            return new ChatBubbleViewModel
            {
                Id = m.Id,
                Author = m.Author,
                Content = m.Content,
                Avatar = m.Author == MessageAuthor.User ? "🧑" : (gRole?.Avatar ?? "🤖"),
                RoleName = m.Author == MessageAuthor.User ? "我" : (gRole?.Name ?? "AI"),
                IsGroupBubble = true,
                CreatedAt = m.CreatedAt,
                IsStreaming = false
            };
        }

        var role = Role;
        return new ChatBubbleViewModel
        {
            Id = m.Id,
            Author = m.Author,
            Content = m.Content,
            Avatar = m.Author == MessageAuthor.User ? "🧑" : (role?.Avatar ?? "🤖"),
            RoleName = m.Author == MessageAuthor.User ? "我" : (role?.Name ?? "AI"),
            CreatedAt = m.CreatedAt,
            IsStreaming = false
        };
    }
}
