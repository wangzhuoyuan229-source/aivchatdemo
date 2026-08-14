using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>Top-level shell VM: navigation rail + middle list + right detail (WeChat-like).</summary>
public partial class MainViewModel : ViewModelBase, INavigation
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(IServiceProvider services, ILogger<MainViewModel> logger)
    {
        _services = services;
        _logger = logger;
    }

    public ChatViewModel Chat => _services.GetRequiredService<ChatViewModel>();
    public RoleListViewModel Roles => _services.GetRequiredService<RoleListViewModel>();
    public ConversationListViewModel Conversations => _services.GetRequiredService<ConversationListViewModel>();
    public KnowledgeViewModel Knowledge => _services.GetRequiredService<KnowledgeViewModel>();
    public SettingsViewModel Settings => _services.GetRequiredService<SettingsViewModel>();
    public CreateRoleViewModel CreateRole => _services.GetRequiredService<CreateRoleViewModel>();

    [ObservableProperty] private ViewModelBase? _middleView;
    [ObservableProperty] private ViewModelBase? _rightView;
    [ObservableProperty] private string _currentPageKey = "roles";
    [ObservableProperty] private string _windowTitle = "AI 角色扮演聊天";

    public async Task InitializeAsync()
    {
        try
        {
            await Roles.LoadAsync();
            await Knowledge.LoadAsync();
            await Settings.LoadAsync();
        }
        catch (Exception ex)
        {
            // Log without crashing startup.
            System.Diagnostics.Debug.WriteLine($"Init error: {ex}");
        }
        Navigate("roles");
    }

    [RelayCommand]
    private void ShowRoles() => Navigate("roles");

    [RelayCommand]
    private void ShowKnowledge() => Navigate("knowledge");

    [RelayCommand]
    private void ShowConversations() => Navigate("conversations");

    [RelayCommand]
    private void ShowSettings() => Navigate("settings");

    public void Navigate(string pageKey)
    {
        CurrentPageKey = pageKey;
        _logger.LogInformation("Navigate to {PageKey}. Chat.Conversation = {ConvId}, Messages count = {MsgCount}",
            pageKey, Chat.Conversation?.Id, Chat.Messages.Count);
        switch (pageKey)
        {
            case "conversations":
                _ = Conversations.LoadAsync();
                MiddleView = Conversations;
                RightView = null;
                break;
            case "roles":
                MiddleView = Roles;
                // 如果之前有打开的会话，恢复显示右侧对话界面（保留上次的对话内容）
                RightView = Chat.Conversation is not null ? Chat : null;
                break;
            case "knowledge":
                MiddleView = null;
                RightView = Knowledge;
                break;
            case "settings":
                MiddleView = null;
                RightView = Settings;
                break;
        }
    }

    /// <summary>
    /// 打开与指定角色的对话。如果该角色已有历史会话，加载最近一次会话（含历史消息）；
    /// 否则创建新会话并生成问候语。
    /// </summary>
    public async Task OpenChatForRoleAsync(Role role)
    {
        var history = _services.GetRequiredService<IChatHistoryService>();
        // 查找该角色最近一次会话（按 UpdatedAt 降序）
        var convs = await history.GetConversationsAsync(role.Id);
        if (convs.Count > 0)
        {
            // 恢复最近一次会话及其历史消息
            await Chat.LoadAsync(convs[0].Id);
        }
        else
        {
            // 无历史会话，创建新会话并生成问候语
            await Chat.StartForRoleAsync(role);
        }
        RightView = Chat;
    }

    public async Task OpenConversationAsync(int conversationId)
    {
        RightView = Chat;
        // Route by conversation type: group chats load members + per-speaker bubbles;
        // private chats keep the existing single-role path.
        var history = _services.GetRequiredService<IChatHistoryService>();
        var conv = await history.GetConversationAsync(conversationId);
        if (conv is null) return;
        if (conv.Type == ConversationType.Group)
            await Chat.LoadGroupAsync(conversationId);
        else
            await Chat.LoadAsync(conversationId);
    }

    public async Task OpenNewGroupChatAsync(IReadOnlyList<Role> members, string title)
    {
        if (members is null || members.Count < 2) return;
        var history = _services.GetRequiredService<IChatHistoryService>();
        var conv = await history.CreateGroupConversationAsync(title, members.Select(r => r.Id).ToList());
        RightView = Chat;
        await Chat.LoadGroupAsync(conv.Id);
        // Refresh the "最近群聊" list so the new group appears immediately.
        await Roles.LoadGroupChatsAsync();
    }
}
