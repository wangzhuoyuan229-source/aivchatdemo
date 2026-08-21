using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private const string UserAvatar = "avares://ChatApp.UI/Assets/user-avatar.jpg";
    private readonly IChatService _chat;
    private readonly IGroupChatService _groupChat;
    private readonly IChatHistoryService _history;
    private readonly IRoleService _roles;
    private readonly IKnowledgeService _knowledge;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ChatViewModel> _logger;
    private readonly INavigation _navigation;
    private CancellationTokenSource? _cts;

    /// <summary>Member roles keyed by RoleId (group mode only).</summary>
    private readonly Dictionary<int, Role> _groupMembers = new();
    /// <summary>Streaming bubbles keyed by RoleId (group mode only).</summary>
    private readonly Dictionary<int, ChatBubbleViewModel> _activeBubbles = new();
    /// <summary>Cached document titles for citation tags (documentId → title).</summary>
    private readonly Dictionary<int, string> _citationTitles = new();

    [ObservableProperty] private Conversation? _conversation;
    [ObservableProperty] private Role? _role;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isGroupMode;
    [ObservableProperty] private string _conversationSubtitle = string.Empty;
    [ObservableProperty] private string _conversationAvatar = "🤖";
    [ObservableProperty] private double _chatFontSize = 14;
    [ObservableProperty] private bool _isEditingMessage;
    [ObservableProperty] private bool _isLoadingEarlier;
    [ObservableProperty] private bool _hasMoreMessages;
    private int _editingMessageId;

    // ----- @ mention (group) -----
    [ObservableProperty] private bool _isMentionPopupOpen;
    [ObservableProperty] private string _mentionFilter = string.Empty;
    [ObservableProperty] private int _selectedMentionIndex;
    public ObservableCollection<Role> FilteredMentionCandidates { get; } = new();

    /// <summary>Messages fetched per "load earlier" page (virtualized list keeps rendering cheap).</summary>
    private const int MessagePageSize = 120;

    public ObservableCollection<ChatBubbleViewModel> Messages { get; } = new();
    public ObservableCollection<string> GroupMemberAvatars { get; } = new();

    public string PinText => Conversation is { IsPinned: true } ? "📌 已置顶" : "📌 置顶";

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(InputText) && Conversation is not null;

    public ChatViewModel(
        IChatService chat,
        IGroupChatService groupChat,
        IChatHistoryService history,
        IRoleService roles,
        IKnowledgeService knowledge,
        IDialogService dialogs,
        INavigation navigation,
        ILogger<ChatViewModel> logger)
    {
        _chat = chat;
        _groupChat = groupChat;
        _history = history;
        _roles = roles;
        _knowledge = knowledge;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
    }

    partial void OnInputTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnConversationChanged(Conversation? value)
    {
        SendCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PinText));
    }

    /// <summary>Applies reading preferences loaded from UI settings.</summary>
    public void ApplyUiSettings(UiSettings settings) => ChatFontSize = settings.ChatFontSize;

    public async Task LoadAsync(int conversationId)
    {
        CancelActiveRequest();
        var conv = await _history.GetConversationAsync(conversationId);
        if (conv is null) return;
        Conversation = conv;
        IsGroupMode = false;
        GroupMemberAvatars.Clear();
        _groupMembers.Clear();
        _activeBubbles.Clear();
        Role = await _roles.GetAsync(conv.RoleId ?? 0);
        Title = Role is null ? conv.Title : Role.Name;
        ConversationAvatar = Role?.Avatar ?? "🤖";
        ConversationSubtitle = Role?.Description ?? string.Empty;
        IsEditingMessage = false;
        Messages.Clear();
        await LoadLatestMessagesAsync(conversationId);
        UpdateMessageOperationFlags();
    }

    /// <summary>Loads the most recent <see cref="MessagePageSize"/> messages into the list.</summary>
    private async Task LoadLatestMessagesAsync(int conversationId)
    {
        var page = await _history.GetMessagesAsync(conversationId, MessagePageSize, beforeId: null);
        foreach (var m in page) Messages.Add(await ToBubbleAsync(m));
        HasMoreMessages = page.Count == MessagePageSize;
    }

    /// <summary>Prepends the page of messages older than the current oldest bubble (3.5 pagination).</summary>
    [RelayCommand]
    private async Task LoadEarlierAsync()
    {
        if (IsLoadingEarlier || Conversation is null || Messages.Count == 0) return;
        IsLoadingEarlier = true;
        try
        {
            var oldestId = Messages[0].Id;
            var page = await _history.GetMessagesAsync(Conversation.Id, MessagePageSize, beforeId: oldestId);
            for (int i = page.Count - 1; i >= 0; i--)
                Messages.Insert(0, await ToBubbleAsync(page[i]));
            HasMoreMessages = page.Count == MessagePageSize;
        }
        finally
        {
            IsLoadingEarlier = false;
        }
    }
    public async Task LoadGroupAsync(int conversationId)
    {
        CancelActiveRequest();
        var conv = await _history.GetConversationAsync(conversationId);
        if (conv is null) return;
        Conversation = conv;
        IsGroupMode = true;
        ConversationAvatar = conv.Avatar;
        Role = null;
        GroupMemberAvatars.Clear();
        _groupMembers.Clear();
        _activeBubbles.Clear();
        IsEditingMessage = false;

        var members = await _history.GetMembersAsync(conversationId);
        foreach (var m in members)
        {
            var r = await _roles.GetAsync(m.RoleId);
            if (r is not null)
            {
                _groupMembers[m.RoleId] = r;
                if (GroupMemberAvatars.Count < 4) GroupMemberAvatars.Add(r.Avatar);
            }
        }

        Title = string.IsNullOrWhiteSpace(conv.Title)
            ? string.Join("、", _groupMembers.Values.Select(r => r.Name))
            : conv.Title;
        ConversationSubtitle = $"{_groupMembers.Count} 位成员 · {string.Join("、", _groupMembers.Values.Select(r => r.Name))}";

        Messages.Clear();
        await LoadLatestMessagesAsync(conversationId);
        UpdateMessageOperationFlags();
    }

    /// <summary>清空当前对话状态（删除当前会话或退出对话时调用）。</summary>
    public void ClearConversation()
    {
        CancelActiveRequest();
        Conversation = null;
        Role = null;
        IsGroupMode = false;
        GroupMemberAvatars.Clear();
        _groupMembers.Clear();
        _activeBubbles.Clear();
        Messages.Clear();
        Title = "对话";
        ConversationAvatar = "🤖";
        ConversationSubtitle = string.Empty;
        StatusText = string.Empty;
        InputText = string.Empty;
        IsEditingMessage = false;
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
        CancelActiveRequest();
        var conv = await _history.CreateConversationAsync(role.Id, $"与{role.Name}的对话");
        Conversation = conv;
        Role = role;
        IsGroupMode = false;
        Title = role.Name;
        ConversationAvatar = role.Avatar;
        ConversationSubtitle = role.Description;
        Messages.Clear();

        try
        {
            var greeting = await _chat.GreetAsync(conv.Id);
            Messages.Add(await ToBubbleAsync(greeting));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Greeting failed.");
            Messages.Add(new ChatBubbleViewModel { Author = MessageAuthor.Assistant, Content = $"（无法生成问候语：{SafeError(ex)}）", Avatar = role.Avatar, RoleName = role.Name });
        }
        UpdateMessageOperationFlags();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (Conversation is null) return;
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        InputText = string.Empty;
        SendCommand.NotifyCanExecuteChanged();

        // Edit-and-resend: truncate everything from the edited user message, then re-send.
        if (IsEditingMessage)
        {
            IsEditingMessage = false;
            await _history.DeleteMessagesFromAsync(Conversation.Id, _editingMessageId);
            if (IsGroupMode)
                await LoadGroupAsync(Conversation.Id);
            else
                await LoadAsync(Conversation.Id);
        }

        IsMentionPopupOpen = false;
        if (IsGroupMode)
        {
            var mentioned = ExtractMentionedRoleIds(text);
            await SendGroupAsync(text, mentioned);
            return;
        }

        if (Role is null) return;

        var userBubble = new ChatBubbleViewModel { Author = MessageAuthor.User, Content = text, Avatar = UserAvatar, RoleName = "我" };
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
            assistantBubble.SetAttachments(final.Attachments);
            await SetCitationsAsync(assistantBubble, final);
        }
        catch (OperationCanceledException)
        {
            assistantBubble.Content += "\n\n[已停止]";
        }
        catch (Exception ex)
        {
            assistantBubble.Content = $"⚠️ {SafeError(ex)}";
            _logger.LogError(ex, "Send failed.");
        }
        finally
        {
            assistantBubble.IsStreaming = false;
            IsSending = false;
            StatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
            UpdateMessageOperationFlags();
        }
    }

    /// <summary>Group-chat send: one user bubble, then N streaming speaker bubbles driven by events.</summary>
    private async Task SendGroupAsync(string text, IReadOnlyList<int>? mentionedRoleIds = null)
    {
        var userBubble = new ChatBubbleViewModel { Author = MessageAuthor.User, Content = text, Avatar = UserAvatar, RoleName = "我" };
        Messages.Add(userBubble);

        IsSending = true;
        StatusText = "群聊发言中...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<GroupChatEvent>(OnGroupEvent);

        try
        {
            await _groupChat.SendAsync(Conversation!.Id, text, progress, _cts.Token, mentionedRoleIds);
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
            Messages.Add(new ChatBubbleViewModel { Author = MessageAuthor.Assistant, Content = $"⚠️ {SafeError(ex)}", Avatar = "⚠️", RoleName = "错误" });
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
            UpdateMessageOperationFlags();
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
                    fb.SetAttachments(finished.FinalMessage.Attachments);
                    _ = SetCitationsAsync(fb, finished.FinalMessage);
                    fb.IsStreaming = false;
                    _activeBubbles.Remove(finished.RoleId);
                }
                break;

            case TurnFinished:
                // IsSending/StatusText are reset in SendGroupAsync's finally.
                break;
        }
    }

    public void UpdateMentionState(string inputText, int caretIndex)
    {
        if (!IsGroupMode || string.IsNullOrEmpty(inputText) || caretIndex <= 0)
        {
            IsMentionPopupOpen = false;
            return;
        }
        var textBeforeCaret = inputText.Substring(0, Math.Min(caretIndex, inputText.Length));
        var lastAt = textBeforeCaret.LastIndexOf('@');
        if (lastAt < 0)
        {
            IsMentionPopupOpen = false;
            return;
        }
        var filter = textBeforeCaret.Substring(lastAt + 1);
        if (filter.Contains(' ') || filter.Contains('\n') || filter.Contains('\r') || filter.Contains('@'))
        {
            IsMentionPopupOpen = false;
            return;
        }
        MentionFilter = filter;
        FilteredMentionCandidates.Clear();
        var normalized = filter.Trim().ToLowerInvariant();
        foreach (var role in _groupMembers.Values.OrderBy(r => r.Name))
        {
            if (string.IsNullOrEmpty(normalized) || role.Name.ToLowerInvariant().Contains(normalized))
                FilteredMentionCandidates.Add(role);
        }
        if (FilteredMentionCandidates.Count > 0)
        {
            SelectedMentionIndex = 0;
            IsMentionPopupOpen = true;
        }
        else
        {
            IsMentionPopupOpen = false;
        }
    }

    public void InsertMention(Role role)
    {
        if (role == null) return;
        var text = InputText;
        var lastAt = text.LastIndexOf('@');
        if (lastAt < 0) return;
        var afterAt = text.Substring(lastAt + 1);
        var end = afterAt.Length;
        var space = afterAt.IndexOf(' ');
        if (space >= 0 && space < end) end = space;
        var nl = afterAt.IndexOf('\n');
        if (nl >= 0 && nl < end) end = nl;
        var before = text.Substring(0, lastAt);
        var after = (lastAt + 1 + end) < text.Length ? text.Substring(lastAt + 1 + end) : string.Empty;
        InputText = before + "@" + role.Name + " " + after;
        IsMentionPopupOpen = false;
    }

    private IReadOnlyList<int> ExtractMentionedRoleIds(string text)
    {
        var list = new List<int>();
        if (!IsGroupMode || string.IsNullOrWhiteSpace(text)) return list;
        foreach (var role in _groupMembers.Values)
        {
            if (text.Contains("@" + role.Name, StringComparison.Ordinal))
                list.Add(role.Id);
        }
        return list.Distinct().ToList();
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
    }

    // ----- 2.1 记忆管理入口 -----

    [RelayCommand]
    private Task OpenMemoryAsync() => _navigation.OpenMemoryManagementAsync();

    // ----- 2.2 消息操作：复制 / 重新生成 / 编辑重发 -----

    [RelayCommand]
    private async Task CopyMessageAsync(ChatBubbleViewModel bubble)
    {
        if (bubble is null) return;
        var text = bubble.Content;
        if (bubble.Attachments.Count > 0)
        {
            var attachmentLines = bubble.Attachments
                .Select(a => $"[图片附件：{(string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title)}]")
                .ToList();
            if (attachmentLines.Count > 0)
                text = text + "\n" + string.Join("\n", attachmentLines);
        }
        await ClipboardService.CopyTextAsync(text);
        StatusText = "已复制到剪贴板";
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (Conversation is null || IsGroupMode || IsSending) return;
        IsSending = true;
        StatusText = "正在重新生成...";
        _cts = new CancellationTokenSource();

        var streamingBubble = new ChatBubbleViewModel
        {
            Author = MessageAuthor.Assistant,
            Content = string.Empty,
            Avatar = Role?.Avatar ?? "🤖",
            RoleName = Role?.Name ?? "AI",
            IsStreaming = true
        };

        try
        {
            // Remove the trailing assistant bubble from the UI first (RegenerateAsync
            // deletes the persisted row), then stream the replacement.
            var last = Messages.LastOrDefault();
            if (last is not null && last.IsAssistant)
                Messages.Remove(last);
            Messages.Add(streamingBubble);

            var progress = new Progress<string>(delta => streamingBubble.Content += delta);
            var final = await _chat.RegenerateAsync(Conversation.Id, progress, _cts.Token);
            streamingBubble.Id = final.Id;
            streamingBubble.Content = final.Content;
            streamingBubble.SetAttachments(final.Attachments);
            await SetCitationsAsync(streamingBubble, final);
        }
        catch (OperationCanceledException)
        {
            streamingBubble.Content += "\n\n[已停止]";
            await RefreshCurrentAsync();
        }
        catch (Exception ex)
        {
            streamingBubble.Content = $"⚠️ {SafeError(ex)}";
            _logger.LogError(ex, "Regenerate failed.");
        }
        finally
        {
            streamingBubble.IsStreaming = false;
            IsSending = false;
            StatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
            UpdateMessageOperationFlags();
        }
    }

    [RelayCommand]
    private void StartEditMessage(ChatBubbleViewModel bubble)
    {
        if (bubble is not { IsUser: true }) return;
        _editingMessageId = bubble.Id;
        InputText = bubble.Content;
        IsEditingMessage = true;
    }

    [RelayCommand]
    private void CancelEditMessage()
    {
        IsEditingMessage = false;
        InputText = string.Empty;
    }

    [RelayCommand]
    private async Task RecallMessageAsync(ChatBubbleViewModel bubble)
    {
        if (bubble is null || !bubble.CanRecall) return;
        if (DateTime.UtcNow - bubble.CreatedAt > TimeSpan.FromMinutes(2))
        {
            StatusText = "已超过2分钟，无法撤回";
            UpdateMessageOperationFlags();
            return;
        }
        try
        {
            await _history.DeleteMessageAsync(bubble.Id);
            Messages.Remove(bubble);
            if (bubble.IsUser)
            {
                InputText = bubble.Content;
                StatusText = "已撤回，可重新编辑";
            }
            UpdateMessageOperationFlags();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recall failed for {MessageId}", bubble.Id);
            StatusText = $"撤回失败：{SafeError(ex)}";
        }
    }

    private void UpdateMessageOperationFlags()
    {
        var last = Messages.LastOrDefault();
        var lastUser = Messages.LastOrDefault(m => m.IsUser);
        foreach (var bubble in Messages)
        {
            bubble.CanRegenerate = !IsGroupMode &&
                ReferenceEquals(bubble, last) && bubble.IsAssistant && !IsSending;
            bubble.CanEdit = bubble.IsUser;
            bubble.CanRecall = bubble.IsUser &&
                ReferenceEquals(bubble, lastUser) &&
                (DateTime.UtcNow - bubble.CreatedAt) < TimeSpan.FromMinutes(2) &&
                !IsSending;
        }
    }

    // ----- 2.3 会话整理：重命名 / 置顶 / 导出 -----

    [RelayCommand]
    private async Task RenameConversationAsync()
    {
        if (Conversation is null) return;
        var (confirmed, text) = await _dialogs.PromptAsync("请输入新的会话名称：", Conversation.Title, "重命名会话");
        if (!confirmed || string.IsNullOrWhiteSpace(text)) return;
        await _history.RenameConversationAsync(Conversation.Id, text);
        Conversation = await _history.GetConversationAsync(Conversation.Id) ?? Conversation;
        if (IsGroupMode || Role is null) Title = Conversation.Title;
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (Conversation is null) return;
        var pinned = !Conversation.IsPinned;
        await _history.SetConversationPinnedAsync(Conversation.Id, pinned);
        Conversation = await _history.GetConversationAsync(Conversation.Id) ?? Conversation;
        OnPropertyChanged(nameof(PinText));
        StatusText = pinned ? "已置顶" : "已取消置顶";
    }

    /// <summary>Exports the current conversation as Markdown ("md") or JSON ("json").</summary>
    public async Task ExportAsync(string format)
    {
        if (Conversation is null) return;
        try
        {
            var messages = await _history.GetMessagesAsync(Conversation.Id);
            string roleNameResolver(int roleId) =>
                _groupMembers.TryGetValue(roleId, out var r) ? r.Name :
                (Role is not null && Role.Id == roleId ? Role.Name : "AI");

            var isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            var content = isJson
                ? ChatExportService.ToJson(Title, messages, roleNameResolver)
                : ChatExportService.ToMarkdown(Title, messages, roleNameResolver);
            var extension = isJson ? "json" : "md";
            var path = await FileSaveService.PickSavePathAsync(
                ChatExportService.SanitizeFileName(Title), "会话导出", $"*.{extension}");
            if (string.IsNullOrWhiteSpace(path)) return;
            await File.WriteAllTextAsync(path, content);
            StatusText = $"已导出到 {path}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Export conversation failed.");
            await _dialogs.ShowErrorAsync($"导出失败：{SafeError(ex)}");
        }
    }

    // ----- 2.4 知识引用溯源 -----

    [RelayCommand]
    private async Task OpenCitationAsync(ChatCitationViewModel citation)
    {
        if (citation is null) return;
        await _navigation.RevealKnowledgeDocumentAsync(citation.DocumentId);
    }

    private async Task SetCitationsAsync(ChatBubbleViewModel bubble, Message message)
    {
        var ids = message.GetCitedDocumentIdList();
        bubble.Citations.Clear();
        if (ids.Count == 0) return;
        foreach (var id in ids)
        {
            bubble.Citations.Add(new ChatCitationViewModel
            {
                DocumentId = id,
                Title = await ResolveCitationTitleAsync(id)
            });
        }
    }

    private async Task<string> ResolveCitationTitleAsync(int documentId)
    {
        if (_citationTitles.TryGetValue(documentId, out var cached)) return cached;
        try
        {
            var doc = await _knowledge.GetDocumentAsync(documentId);
            var title = doc?.Title ?? string.Empty;
            _citationTitles[documentId] = title;
            return title;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void CancelActiveRequest()
    {
        if (_cts is { IsCancellationRequested: false })
            _cts.Cancel();
    }

    private async Task<ChatBubbleViewModel> ToBubbleAsync(Message m)
    {
        // Group mode: resolve avatar/name per message via member map.
        ChatBubbleViewModel bubble;
        if (IsGroupMode)
        {
            var gRole = m.Author == MessageAuthor.User ? null : _groupMembers.GetValueOrDefault(m.RoleId);
            bubble = new ChatBubbleViewModel
            {
                Id = m.Id,
                Author = m.Author,
                Content = m.Content,
                Avatar = m.Author == MessageAuthor.User ? UserAvatar : (gRole?.Avatar ?? "🤖"),
                RoleName = m.Author == MessageAuthor.User ? "我" : (gRole?.Name ?? "AI"),
                IsGroupBubble = true,
                CreatedAt = m.CreatedAt,
                IsStreaming = false
            };
        }
        else
        {
            var role = Role;
            bubble = new ChatBubbleViewModel
            {
                Id = m.Id,
                Author = m.Author,
                Content = m.Content,
                Avatar = m.Author == MessageAuthor.User ? UserAvatar : (role?.Avatar ?? "🤖"),
                RoleName = m.Author == MessageAuthor.User ? "我" : (role?.Name ?? "AI"),
                CreatedAt = m.CreatedAt,
                IsStreaming = false
            };
        }
        bubble.SetAttachments(m.Attachments);
        await SetCitationsAsync(bubble, m);
        return bubble;
    }
}
