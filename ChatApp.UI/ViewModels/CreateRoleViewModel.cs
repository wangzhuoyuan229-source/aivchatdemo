using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>Creates and edits roles, their authored style examples and knowledge bindings.</summary>
public partial class CreateRoleViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly IKnowledgeService _knowledgeService;
    private readonly BundledKnowledgeService _bundledKnowledgeService;
    private readonly INavigation _navigation;
    private readonly ILogger<CreateRoleViewModel> _logger;
    private Role? _editingRole;
    private bool _openChatAfterSave;
    private bool _suppressAutomaticAvatarMatch;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImageAvatar))]
    private string _avatar = "🎭";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _background = string.Empty;
    [ObservableProperty] private string _userPersona = string.Empty;
    [ObservableProperty] private string _personality = string.Empty;
    [ObservableProperty] private string _speakingStyle = string.Empty;
    [ObservableProperty] private string _dialogueExamples = string.Empty;
    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private string _supplementaryPrompt = string.Empty;
    [ObservableProperty] private bool _isSupplementaryExpanded;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _windowTitle = "创建 AI 角色";
    [ObservableProperty] private string _submitText = "创建并对话";
    [ObservableProperty] private string _knowledgeBindingHint = "未选择知识分组时，角色不会读取任何知识库资料。";
    [ObservableProperty] private string _avatarMatchStatus = string.Empty;
    [ObservableProperty] private bool _isMatchingAvatar;
    [ObservableProperty] private bool _isSaving;

    public ObservableCollection<SelectableKnowledgeGroup> KnowledgeGroups { get; } = new();

    public Action? RequestClose { get; set; }

    public CreateRoleViewModel(
        IRoleService roleService,
        IKnowledgeService knowledgeService,
        BundledKnowledgeService bundledKnowledgeService,
        INavigation navigation,
        ILogger<CreateRoleViewModel> logger)
    {
        _roleService = roleService;
        _knowledgeService = knowledgeService;
        _bundledKnowledgeService = bundledKnowledgeService;
        _navigation = navigation;
        _logger = logger;
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && !IsSaving && !IsMatchingAvatar;

    public bool HasImageAvatar => !string.IsNullOrWhiteSpace(Avatar) &&
                                  (Avatar.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
                                   File.Exists(Avatar));

    partial void OnNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsMatchingAvatarChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    public async Task PrepareForCreateAsync(CancellationToken ct = default)
    {
        _editingRole = null;
        _openChatAfterSave = true;
        _suppressAutomaticAvatarMatch = false;
        Name = string.Empty;
        Avatar = "🎭";
        Description = string.Empty;
        Background = string.Empty;
        UserPersona = string.Empty;
        Personality = string.Empty;
        SpeakingStyle = string.Empty;
        DialogueExamples = string.Empty;
        Greeting = string.Empty;
        SupplementaryPrompt = string.Empty;
        IsSupplementaryExpanded = false;
        ErrorText = string.Empty;
        AvatarMatchStatus = string.Empty;
        WindowTitle = "创建 AI 角色";
        SubmitText = "创建并对话";
        await LoadKnowledgeGroupsAsync(Array.Empty<int>(), selectBuiltInByDefault: true, ct);
    }

    public async Task PrepareForEditAsync(Role role, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        _editingRole = await _roleService.GetAsync(role.Id, ct)
            ?? throw new InvalidOperationException("角色不存在。");
        _openChatAfterSave = false;
        _suppressAutomaticAvatarMatch = true;
        var boundIds = await _roleService.GetKnowledgeGroupIdsAsync(role.Id, ct);

        Name = _editingRole.Name;
        Avatar = _editingRole.Avatar;
        Description = _editingRole.Description;
        Background = _editingRole.Background;
        UserPersona = _editingRole.UserPersona;
        Personality = _editingRole.Personality;
        SpeakingStyle = _editingRole.SpeakingStyle;
        DialogueExamples = _editingRole.DialogueExamples;
        Greeting = _editingRole.Greeting;
        SupplementaryPrompt = _editingRole.SystemPrompt ?? string.Empty;
        if (string.IsNullOrWhiteSpace(SupplementaryPrompt) &&
            (!string.IsNullOrWhiteSpace(Background) || !string.IsNullOrWhiteSpace(Personality) || !string.IsNullOrWhiteSpace(SpeakingStyle)))
        {
            SupplementaryPrompt = string.Join("\n", new[] { Background, Personality, SpeakingStyle }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        IsSupplementaryExpanded = !string.IsNullOrWhiteSpace(DialogueExamples) || !string.IsNullOrWhiteSpace(Greeting) || !string.IsNullOrWhiteSpace(Description);
        ErrorText = string.Empty;
        AvatarMatchStatus = string.Empty;
        WindowTitle = $"编辑角色：{_editingRole.Name}";
        SubmitText = "保存";
        await LoadKnowledgeGroupsAsync(boundIds, selectBuiltInByDefault: false, ct);
    }

    private async Task LoadKnowledgeGroupsAsync(
        IReadOnlyCollection<int> selectedIds,
        bool selectBuiltInByDefault,
        CancellationToken ct)
    {
        KnowledgeGroups.Clear();
        var selected = selectedIds.ToHashSet();
        var groups = await _knowledgeService.ListGroupsAsync(ct);
        foreach (var group in groups)
        {
            var isSelected = selected.Contains(group.Id) ||
                             selectBuiltInByDefault &&
                             string.Equals(group.Name, BundledKnowledgeService.GroupName, StringComparison.Ordinal);
            KnowledgeGroups.Add(new SelectableKnowledgeGroup(group, isSelected));
        }

        KnowledgeBindingHint = groups.Count == 0
            ? "知识库中还没有分组。请先创建分组并导入资料；当前角色不会读取全局或未分组文档。"
            : "只会检索勾选分组中的资料；创建角色时也会从这些分组自动匹配最相关的头像图片。";
    }

    [RelayCommand]
    private async Task MatchAvatarAsync() => await TryMatchAvatarAsync(showMissingGroupHint: true);

    [RelayCommand]
    private void UseEmojiAvatar()
    {
        Avatar = "🎭";
        _suppressAutomaticAvatarMatch = true;
        AvatarMatchStatus = "已改用 emoji；保存时不会自动替换。";
    }

    private async Task<bool> TryMatchAvatarAsync(bool showMissingGroupHint)
    {
        if (IsMatchingAvatar || string.IsNullOrWhiteSpace(Name))
        {
            if (string.IsNullOrWhiteSpace(Name)) AvatarMatchStatus = "请先填写角色名称。";
            return false;
        }

        var groupIds = KnowledgeGroups.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        if (groupIds.Length == 0)
        {
            if (showMissingGroupHint) AvatarMatchStatus = "请先勾选至少一个包含图片的知识分组。";
            return false;
        }

        IsMatchingAvatar = true;
        AvatarMatchStatus = "正在从所选知识分组检索相关图片…";
        try
        {
            var pathCandidates = await _knowledgeService.FindRoleAvatarCandidatesAsync(Name, groupIds, 20);
            var pathCandidate = pathCandidates.FirstOrDefault();
            if (pathCandidate is not null)
            {
                try
                {
                    AvatarMatchStatus = $"已找到“{pathCandidate.FileName}”，正在定位并放大人物面部…";
                    Avatar = await _knowledgeService.CreateRoleAvatarDataUriAsync(pathCandidate.DocumentId);
                    _suppressAutomaticAvatarMatch = false;
                    AvatarMatchStatus = $"已从“{pathCandidate.FileName}”生成面部优先头像。";
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Indexed avatar candidate {DocumentId} could not be rendered; trying bundled files.", pathCandidate.DocumentId);
                }
            }

            var builtInSelected = KnowledgeGroups.Any(item => item.IsSelected &&
                string.Equals(item.Name, BundledKnowledgeService.GroupName, StringComparison.Ordinal));
            var bundledCandidate = builtInSelected
                ? _bundledKnowledgeService.FindRoleAvatarCandidate(Name)
                : null;
            if (bundledCandidate is not null)
            {
                try
                {
                    AvatarMatchStatus = $"已找到“{Path.GetFileName(bundledCandidate.RelativePath)}”，正在定位并放大人物面部…";
                    Avatar = await _knowledgeService.CreateRoleAvatarDataUriFromFileAsync(bundledCandidate.SourcePath);
                    _suppressAutomaticAvatarMatch = false;
                    AvatarMatchStatus = $"已从“{Path.GetFileName(bundledCandidate.RelativePath)}”生成面部优先头像。";
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bundled avatar candidate {Path} could not be rendered; trying semantic retrieval.", bundledCandidate.RelativePath);
                }
            }

            var result = await _knowledgeService.RetrieveAsync(new KnowledgeRetrievalRequest
            {
                Query = BuildAvatarQuery(Name, Description, Background, Personality),
                AllowedGroupIds = groupIds,
                TopK = 1,
                MinScore = 1,
                ImageTopK = 20,
                ImageMinScore = 0.35,
                NeighborRadius = 0,
                ContextCharBudget = 200
            });
            var candidate = SelectBestAvatarCandidate(Name, result.ImageHits);
            if (candidate is null)
            {
                AvatarMatchStatus = "没有找到达到相关度阈值的知识图片，将保留当前头像。";
                return false;
            }

            AvatarMatchStatus = $"已匹配“{candidate.Title}”，正在定位并放大人物面部…";
            Avatar = await _knowledgeService.CreateRoleAvatarDataUriAsync(candidate.DocumentId);
            _suppressAutomaticAvatarMatch = false;
            AvatarMatchStatus = $"已从“{candidate.Title}”生成面部优先头像（相关度 {candidate.Score:P0}）。";
            return true;
        }
        catch (Exception ex)
        {
            AvatarMatchStatus = $"头像图片匹配暂不可用，将保留当前头像：{ex.GetBaseException().Message}";
            _logger.LogWarning(ex, "Unable to match a knowledge image for new role {RoleName}.", Name);
            return false;
        }
        finally
        {
            IsMatchingAvatar = false;
        }
    }

    internal static string BuildAvatarQuery(string name, string description, string background, string personality)
    {
        var text = $"角色名称：{name}\n寻找该角色的头像、肖像或立绘。\n" +
                   $"简介：{description}\n背景：{background}\n性格：{personality}";
        return text.Length <= 1800 ? text : text[..1800];
    }

    internal static KnowledgeImageHit? SelectBestAvatarCandidate(
        string roleName,
        IReadOnlyList<KnowledgeImageHit> candidates)
    {
        var name = NormalizeForMatch(roleName);
        return candidates
            .OrderByDescending(candidate =>
                candidate.Score +
                (ContainsName(candidate.Title, name) || ContainsName(candidate.FileName, name) ? 2 : 0) +
                (ContainsName(candidate.Tags, name) ? 1 : 0) +
                (ContainsName(candidate.Description, name) ? 0.5 : 0))
            .FirstOrDefault();
    }

    private static bool ContainsName(string value, string normalizedName) =>
        normalizedName.Length > 0 && NormalizeForMatch(value).Contains(normalizedName, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeForMatch(string value) => string.Concat(
        (value ?? string.Empty).Where(character => !char.IsWhiteSpace(character) && character is not '_' and not '-'));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorText = string.Empty;
        if (!CanSave) return;

        IsSaving = true;
        try
        {
            var isNew = _editingRole is null;
            if (isNew && !_suppressAutomaticAvatarMatch &&
                (string.IsNullOrWhiteSpace(Avatar) || Avatar == "🎭"))
                await TryMatchAvatarAsync(showMissingGroupHint: false);

            var role = _editingRole ?? new Role { IsPreset = false };
            role.Name = Name.Trim();
            role.Avatar = string.IsNullOrWhiteSpace(Avatar) ? "🎭" : Avatar.Trim();
            role.Description = Description.Trim();
            role.Background = Background.Trim();
            role.UserPersona = UserPersona.Trim();
            role.Personality = Personality.Trim();
            role.SpeakingStyle = SpeakingStyle.Trim();
            role.DialogueExamples = DialogueExamples.Trim();
            role.Greeting = Greeting.Trim();
            role.SystemPrompt = SupplementaryPrompt.Trim();

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
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
