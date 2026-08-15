using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>Creates and edits roles, their authored style examples and knowledge bindings.</summary>
public partial class CreateRoleViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly INavigation _navigation;
    private readonly ILogger<CreateRoleViewModel> _logger;
    private Role? _editingRole;
    private bool _openChatAfterSave;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _avatar = "🎭";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _background = string.Empty;
    [ObservableProperty] private string _personality = string.Empty;
    [ObservableProperty] private string _speakingStyle = string.Empty;
    [ObservableProperty] private string _dialogueExamples = string.Empty;
    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _windowTitle = "创建 AI 角色";
    [ObservableProperty] private string _submitText = "创建并对话";
    [ObservableProperty] private string _knowledgeBindingHint = "未选择知识分组时，角色不会读取任何知识库资料。";

    public ObservableCollection<SelectableKnowledgeGroup> KnowledgeGroups { get; } = new();

    public Action? RequestClose { get; set; }

    public CreateRoleViewModel(
        IRoleService roleService,
        IKnowledgeService knowledgeService,
        INavigation navigation,
        ILogger<CreateRoleViewModel> logger)
    {
        _roleService = roleService;
        _knowledgeService = knowledgeService;
        _navigation = navigation;
        _logger = logger;
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    partial void OnNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    public async Task PrepareForCreateAsync(CancellationToken ct = default)
    {
        _editingRole = null;
        _openChatAfterSave = true;
        Name = string.Empty;
        Avatar = "🎭";
        Description = string.Empty;
        Background = string.Empty;
        Personality = string.Empty;
        SpeakingStyle = string.Empty;
        DialogueExamples = string.Empty;
        Greeting = string.Empty;
        ErrorText = string.Empty;
        WindowTitle = "创建 AI 角色";
        SubmitText = "创建并对话";
        await LoadKnowledgeGroupsAsync(Array.Empty<int>(), ct);
    }

    public async Task PrepareForEditAsync(Role role, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        _editingRole = await _roleService.GetAsync(role.Id, ct)
            ?? throw new InvalidOperationException("角色不存在。");
        _openChatAfterSave = false;
        var boundIds = await _roleService.GetKnowledgeGroupIdsAsync(role.Id, ct);

        Name = _editingRole.Name;
        Avatar = _editingRole.Avatar;
        Description = _editingRole.Description;
        Background = _editingRole.Background;
        Personality = _editingRole.Personality;
        SpeakingStyle = _editingRole.SpeakingStyle;
        DialogueExamples = _editingRole.DialogueExamples;
        Greeting = _editingRole.Greeting;
        ErrorText = string.Empty;
        WindowTitle = $"编辑角色：{_editingRole.Name}";
        SubmitText = "保存";
        await LoadKnowledgeGroupsAsync(boundIds, ct);
    }

    private async Task LoadKnowledgeGroupsAsync(IReadOnlyCollection<int> selectedIds, CancellationToken ct)
    {
        KnowledgeGroups.Clear();
        var selected = selectedIds.ToHashSet();
        var groups = await _knowledgeService.ListGroupsAsync(ct);
        foreach (var group in groups)
            KnowledgeGroups.Add(new SelectableKnowledgeGroup(group, selected.Contains(group.Id)));

        KnowledgeBindingHint = groups.Count == 0
            ? "知识库中还没有分组。请先创建分组并导入资料；当前角色不会读取全局或未分组文档。"
            : "只会检索勾选分组中的资料；不勾选时，涉及未知设定会由角色自然地说明无法确认。";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        if (!CanSave) return;

        try
        {
            var isNew = _editingRole is null;
            var role = _editingRole ?? new Role { IsPreset = false };
            role.Name = Name.Trim();
            role.Avatar = string.IsNullOrWhiteSpace(Avatar) ? "🎭" : Avatar.Trim();
            role.Description = Description.Trim();
            role.Background = Background.Trim();
            role.Personality = Personality.Trim();
            role.SpeakingStyle = SpeakingStyle.Trim();
            role.DialogueExamples = DialogueExamples.Trim();
            role.Greeting = Greeting.Trim();

            if (isNew)
            {
                role = await _roleService.CreateAsync(role);
                _editingRole = role;
            }
            else
            {
                await _roleService.UpdateAsync(role);
            }

            var selectedGroupIds = KnowledgeGroups.Where(g => g.IsSelected).Select(g => g.Id).ToArray();
            await _roleService.SetKnowledgeGroupIdsAsync(role.Id, selectedGroupIds);
            RequestClose?.Invoke();

            if (_openChatAfterSave)
            {
                await _navigation.OpenChatForRoleAsync(role);
                _openChatAfterSave = false;
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.LogError(ex, "Save role failed.");
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
