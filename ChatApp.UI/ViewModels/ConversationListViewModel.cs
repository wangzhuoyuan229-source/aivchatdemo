using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>A conversation entry with its role name resolved for display.</summary>
public class ConversationItemViewModel : ViewModelBase
{
    public Conversation Conversation { get; init; } = new();
    public string RoleName { get; init; } = string.Empty;
    public string Avatar { get; init; } = "💬";
    public bool IsGroup { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Conversation.Title) ? RoleName : Conversation.Title;
    public string UpdatedText => Conversation.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
}

public partial class ConversationListViewModel : ViewModelBase
{
    private readonly IChatHistoryService _history;
    private readonly IRoleService _roles;
    private readonly INavigation _navigation;
    private readonly ILogger<ConversationListViewModel> _logger;
    private readonly IDialogService _dialogs;

    public ObservableCollection<ConversationItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;

    public ConversationListViewModel(IChatHistoryService history, IRoleService roles, INavigation navigation,
        ILogger<ConversationListViewModel> logger, IDialogService dialogs)
    {
        _history = history;
        _roles = roles;
        _navigation = navigation;
        _logger = logger;
        _dialogs = dialogs;
    }

    public async Task LoadAsync()
    {
        Items.Clear();
        try
        {
            var convs = await _history.GetConversationsAsync();
            // Build a full role lookup once (roles are few) to cover both private and group displays.
            var allRoles = await _roles.GetAllAsync();
            var roleMap = allRoles.ToDictionary(r => r.Id);

            foreach (var c in convs)
            {
                if (c.Type == ConversationType.Group || c.RoleId is null)
                {
                    // Group: resolve member names for the subtitle.
                    var members = await _history.GetMembersAsync(c.Id);
                    var names = members
                        .Select(m => roleMap.TryGetValue(m.RoleId, out var r) ? r.Name : null)
                        .Where(n => n is not null)
                        .ToList();
                    Items.Add(new ConversationItemViewModel
                    {
                        Conversation = c,
                        RoleName = names.Count > 0 ? string.Join("、", names) : "空群聊",
                        Avatar = "👥",
                        IsGroup = true
                    });
                }
                else
                {
                    roleMap.TryGetValue(c.RoleId.Value, out var r);
                    Items.Add(new ConversationItemViewModel
                    {
                        Conversation = c,
                        RoleName = r?.Name ?? "未知角色",
                        Avatar = r?.Avatar ?? "💬",
                        IsGroup = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load conversations.");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task OpenAsync(ConversationItemViewModel item) => await _navigation.OpenConversationAsync(item.Conversation.Id);

    [RelayCommand]
    private async Task DeleteAsync(ConversationItemViewModel item)
    {
        // 弹出确认对话框，避免误删
        var confirm = await _dialogs.ConfirmAsync(
            $"确定要删除会话「{item.Title}」吗？\n\n该操作不可撤销，将删除该会话的所有消息。",
            "删除会话");
        if (!confirm) return;

        try
        {
            await _history.DeleteConversationAsync(item.Conversation.Id);
            Items.Remove(item);

            // 如果删除的是当前正在 ChatViewModel 中显示的会话，清空对话界面
            if (_navigation is MainViewModel main && main.Chat.Conversation?.Id == item.Conversation.Id)
            {
                main.Chat.ClearConversation();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete conversation failed.");
            await _dialogs.ShowErrorAsync($"删除会话失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) { await LoadAsync(); return; }
        // Search across all conversations; open results in a simple manner.
        try
        {
            var results = await _history.SearchAsync(SearchText);
            Items.Clear();
            var roleMap = new Dictionary<int, Role>();
            foreach (var rid in results.Select(m => m.RoleId).Distinct())
            {
                var r = await _roles.GetAsync(rid);
                if (r is not null) roleMap[rid] = r;
            }
            foreach (var m in results)
            {
                roleMap.TryGetValue(m.RoleId, out var r);
                Items.Add(new ConversationItemViewModel
                {
                    Conversation = new Conversation { Id = m.ConversationId, RoleId = m.RoleId, Title = $"“{Truncate(m.Content, 30)}”", UpdatedAt = m.CreatedAt },
                    RoleName = r?.Name ?? "未知",
                    Avatar = "🔎"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search failed.");
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
