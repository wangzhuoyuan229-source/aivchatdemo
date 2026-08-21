using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

public partial class RoleListViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly IChatHistoryService _history;
    private readonly INavigation _navigation;
    private readonly ILogger<RoleListViewModel> _logger;
    private readonly IDialogService _dialogs;

    public ObservableCollection<Role> Roles { get; } = new();

    /// <summary>Past group chats shown in their own library section so they can be reopened.</summary>
    public ObservableCollection<ConversationItemViewModel> GroupChats { get; } = new();

    /// <summary>True when there is at least one past group chat.</summary>
    public bool HasGroupChats => GroupChats.Count > 0;

    /// <summary>The role library is the default section so group chats never push roles out of view.</summary>
    public bool IsRolesSection => !IsGroupChatsSection;

    [ObservableProperty] private Role? _selectedRole;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRolesSection))]
    private bool _isGroupChatsSection;

    public RoleListViewModel(IRoleService roleService, IChatHistoryService history, INavigation navigation,
        ILogger<RoleListViewModel> logger, IDialogService dialogs)
    {
        _roleService = roleService;
        _history = history;
        _navigation = navigation;
        _logger = logger;
        _dialogs = dialogs;
    }

    public async Task LoadAsync()
    {
        Roles.Clear();
        try
        {
            var roles = await _roleService.GetAllAsync();
            foreach (var r in roles)
            {
                r.KnowledgeGroupCount = (await _roleService.GetKnowledgeGroupIdsAsync(r.Id)).Count;
                Roles.Add(r);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roles.");
        }
        await LoadGroupChatsAsync();
    }

    /// <summary>Loads past group conversations (newest first) for the "最近群聊" list.</summary>
    public async Task LoadGroupChatsAsync()
    {
        GroupChats.Clear();
        OnPropertyChanged(nameof(HasGroupChats));
        try
        {
            var convs = await _history.GetConversationsAsync();
            var groups = convs.Where(c => c.Type == ConversationType.Group).ToList();
            if (groups.Count == 0) return;

            var allRoles = await _roleService.GetAllAsync();
            var roleMap = allRoles.ToDictionary(r => r.Id);

            foreach (var c in groups)
            {
                var members = await _history.GetMembersAsync(c.Id);
                var names = members
                    .Select(m => roleMap.TryGetValue(m.RoleId, out var r) ? r.Name : null)
                    .Where(n => n is not null)
                    .ToList();
                var avatars = members
                    .Select(m => roleMap.TryGetValue(m.RoleId, out var r) ? r.Avatar : null)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Cast<string>()
                    .Take(4)
                    .ToList();
                GroupChats.Add(new ConversationItemViewModel
                {
                    Conversation = c,
                    RoleName = names.Count > 0 ? string.Join("、", names) : "空群聊",
                    Avatar = c.Avatar,
                    MemberAvatars = avatars,
                    IsGroup = true
                });
            }
            OnPropertyChanged(nameof(HasGroupChats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load group chats.");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void ShowRolesSection() => IsGroupChatsSection = false;

    [RelayCommand]
    private void ShowGroupChatsSection() => IsGroupChatsSection = true;

    [RelayCommand]
    private async Task OpenAsync(Role role) => await _navigation.OpenChatForRoleAsync(role);

    [RelayCommand]
    private async Task OpenGroupChatAsync(ConversationItemViewModel item)
    {
        if (item is null) return;
        await _navigation.OpenConversationAsync(item.Conversation.Id);
    }

    [RelayCommand]
    private async Task RenameGroupChatAsync(ConversationItemViewModel item)
    {
        if (item is null) return;
        var (confirmed, text) = await _dialogs.PromptAsync("请输入新的会话名称：", item.Title, "重命名会话");
        if (!confirmed || string.IsNullOrWhiteSpace(text)) return;
        await _history.RenameConversationAsync(item.Conversation.Id, text);
        await LoadGroupChatsAsync();
    }

    [RelayCommand]
    private async Task ToggleGroupChatPinAsync(ConversationItemViewModel item)
    {
        if (item is null) return;
        await _history.SetConversationPinnedAsync(item.Conversation.Id, !item.Conversation.IsPinned);
        await LoadGroupChatsAsync();
    }

    [RelayCommand]
    private async Task DeleteGroupChatAsync(ConversationItemViewModel item)
    {
        if (item is null) return;
        var confirmed = await _dialogs.ConfirmAsync(
            $"确定要删除群聊「{item.Title}」吗？\n\n该操作不可撤销，将一并删除群聊消息、附件和由该群聊产生的长期记忆。角色本身不会被删除。",
            "删除群聊");
        if (!confirmed) return;

        try
        {
            var conversationId = item.Conversation.Id;
            await _history.DeleteConversationAsync(conversationId);
            GroupChats.Remove(item);
            OnPropertyChanged(nameof(HasGroupChats));
            _navigation.CloseConversationIfOpen(conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete group conversation {ConversationId} failed.", item.Conversation.Id);
            await _dialogs.ShowErrorAsync($"删除群聊失败：{SafeError(ex)}");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Role role)
    {
        // 弹出确认对话框，避免误删；预设角色给出更强警告
        var message = role.IsPreset
            ? $"「{role.Name}」是内置角色。\n\n确定要删除吗？该操作不可撤销，将一并删除该角色下的所有会话、消息与长期记忆。"
            : $"确定要删除角色「{role.Name}」吗？\n\n该操作不可撤销，将一并删除该角色下的所有会话、消息与长期记忆。";

        var confirm = await _dialogs.ConfirmAsync(
            message,
            "删除角色");

        if (!confirm) return;

        try
        {
            await _roleService.DeleteAsync(role.Id);
            Roles.Remove(role);
            // 如果删除的是当前选中的角色，清除选中状态
            if (ReferenceEquals(SelectedRole, role)) SelectedRole = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete role failed.");
            await _dialogs.ShowErrorAsync($"删除失败：{SafeError(ex)}");
        }
    }
}
