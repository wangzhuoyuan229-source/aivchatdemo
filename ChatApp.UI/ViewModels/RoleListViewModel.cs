using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
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

    public ObservableCollection<Role> Roles { get; } = new();

    /// <summary>Past group chats, shown above the role list so they can be reopened.</summary>
    public ObservableCollection<ConversationItemViewModel> GroupChats { get; } = new();

    /// <summary>True when there is at least one past group chat (drives section visibility).</summary>
    public bool HasGroupChats => GroupChats.Count > 0;

    [ObservableProperty] private Role? _selectedRole;
    [ObservableProperty] private string _searchText = string.Empty;

    public RoleListViewModel(IRoleService roleService, IChatHistoryService history, INavigation navigation, ILogger<RoleListViewModel> logger)
    {
        _roleService = roleService;
        _history = history;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        Roles.Clear();
        try
        {
            var roles = await _roleService.GetAllAsync();
            foreach (var r in roles) Roles.Add(r);
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
                GroupChats.Add(new ConversationItemViewModel
                {
                    Conversation = c,
                    RoleName = names.Count > 0 ? string.Join("、", names) : "空群聊",
                    Avatar = "👥",
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
    private async Task OpenAsync(Role role) => await _navigation.OpenChatForRoleAsync(role);

    [RelayCommand]
    private async Task OpenGroupChatAsync(ConversationItemViewModel item)
    {
        if (item is null) return;
        await _navigation.OpenConversationAsync(item.Conversation.Id);
    }

    [RelayCommand]
    private async Task DeleteAsync(Role role)
    {
        // 弹出确认对话框，避免误删；预设角色给出更强警告
        var message = role.IsPreset
            ? $"「{role.Name}」是内置角色。\n\n确定要删除吗？该操作不可撤销，将一并删除该角色下的所有会话、消息与长期记忆。"
            : $"确定要删除角色「{role.Name}」吗？\n\n该操作不可撤销，将一并删除该角色下的所有会话、消息与长期记忆。";

        var confirm = System.Windows.MessageBox.Show(
            message,
            "删除角色",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

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
            System.Windows.MessageBox.Show(
                $"删除失败：{ex.Message}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
