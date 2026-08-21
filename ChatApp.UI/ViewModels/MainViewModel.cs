using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Avalonia;

namespace ChatApp.UI.ViewModels;

/// <summary>Top-level shell VM: navigation rail + middle list + right detail (WeChat-like).</summary>
public partial class MainViewModel : ViewModelBase, INavigation
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IUrlLauncher? _urlLauncher;

    public MainViewModel(IServiceProvider services, ILogger<MainViewModel> logger, IUrlLauncher? urlLauncher = null)
    {
        _services = services;
        _logger = logger;
        _urlLauncher = urlLauncher;
    }

    public ChatViewModel Chat => _services.GetRequiredService<ChatViewModel>();
    public RoleListViewModel Roles => _services.GetRequiredService<RoleListViewModel>();
    public KnowledgeViewModel Knowledge => _services.GetRequiredService<KnowledgeViewModel>();
    public SettingsViewModel Settings => _services.GetRequiredService<SettingsViewModel>();
    public CreateRoleViewModel CreateRole => _services.GetRequiredService<CreateRoleViewModel>();

    [ObservableProperty] private ViewModelBase? _middleView;
    [ObservableProperty] private ViewModelBase? _rightView;
    [ObservableProperty] private string _currentPageKey = "roles";
    [ObservableProperty] private string _windowTitle = "AI 角色扮演聊天";
    [ObservableProperty] private bool _isDeveloperHelpOpen;
    public IReadOnlyList<DeveloperSocial> DeveloperSocialItems { get; } = DeveloperSocials.All;

    public async Task InitializeAsync()
    {
        try
        {
            // Load roles / knowledge / settings concurrently; the DbContextFactory
            // creates isolated contexts per call so parallel access is safe.
            await Task.WhenAll(Roles.LoadAsync(), Knowledge.LoadAsync(), Settings.LoadAsync());
        }
        catch (Exception ex)
        {
            // Log without crashing startup.
            System.Diagnostics.Debug.WriteLine($"Init error: {ex}");
            _logger.LogWarning(ex, "Startup initialization partially failed.");
        }
        Navigate("roles");
    }

    [RelayCommand]
    private void ShowRoles() => Navigate("roles");

    [RelayCommand]
    private void ShowKnowledge() => Navigate("knowledge");

    [RelayCommand]
    private void ShowSettings() => Navigate("settings");

    public void Navigate(string pageKey)
    {
        CurrentPageKey = pageKey;
        _logger.LogInformation("Navigate to {PageKey}. Chat.Conversation = {ConvId}, Messages count = {MsgCount}",
            pageKey, Chat.Conversation?.Id, Chat.Messages.Count);
        switch (pageKey)
        {
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

    public void CloseConversationIfOpen(int conversationId)
    {
        if (Chat.Conversation?.Id != conversationId) return;
        Chat.ClearConversation();
        if (ReferenceEquals(RightView, Chat)) RightView = null;
    }

    public async Task OpenNewGroupChatAsync(IReadOnlyList<Role> members, string title, string? avatar = null)
    {
        if (members is null || members.Count < 2) return;
        var history = _services.GetRequiredService<IChatHistoryService>();
        var conv = await history.CreateGroupConversationAsync(title, members.Select(r => r.Id).ToList(), avatar);
        RightView = Chat;
        await Chat.LoadGroupAsync(conv.Id);
        // Refresh the "最近群聊" list so the new group appears immediately.
        await Roles.LoadGroupChatsAsync();
    }

    public async Task RevealKnowledgeDocumentAsync(int documentId)
    {
        Navigate("knowledge");
        await Knowledge.RevealDocumentAsync(documentId);
    }

    [RelayCommand]
    private void ShowDeveloperHelp() => IsDeveloperHelpOpen = true;

    [RelayCommand]
    private void CloseDeveloperHelp() => IsDeveloperHelpOpen = false;

    [RelayCommand]
    private async Task OpenDeveloperSocialAsync(DeveloperSocial? social)
    {
        if (social is null || string.IsNullOrWhiteSpace(social.Url)) return;
        var url = social.Url.Trim();
        if (_urlLauncher is not null && await _urlLauncher.TryOpenAsync(url)) return;
        await ClipboardService.CopyTextAsync(social.CopyText ?? url);
    }

    [RelayCommand]
    private async Task CopyDeveloperSocialAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await ClipboardService.CopyTextAsync(text);
    }

    /// <summary>Opens the memory-management window scoped to the current conversation.</summary>
    public async Task OpenMemoryManagementAsync()
    {
        var memory = _services.GetRequiredService<MemoryManagementViewModel>();
        if (Chat.Conversation is null) return;

        if (Chat.Conversation.Type == ConversationType.Group)
        {
            var history = _services.GetRequiredService<IChatHistoryService>();
            var members = await history.GetMembersAsync(Chat.Conversation.Id);
            var roles = new List<Role>();
            foreach (var member in members)
            {
                var role = await _services.GetRequiredService<IRoleService>().GetAsync(member.RoleId);
                if (role is not null) roles.Add(role);
            }
            if (roles.Count == 0) return;
            await memory.InitializeGroupAsync(roles);
        }
        else
        {
            var role = await _services.GetRequiredService<IRoleService>().GetAsync(Chat.Conversation.RoleId ?? 0);
            if (role is null) return;
            await memory.InitializeAsync(role);
        }

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is { } owner)
        {
            var window = new Views.MemoryManagementWindow { DataContext = memory };
            await window.ShowDialog(owner);
        }
    }
}
