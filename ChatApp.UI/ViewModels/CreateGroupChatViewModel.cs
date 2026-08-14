using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>A role row in the group-chat creation list, with a selection checkbox.</summary>
public partial class RoleSelectionItem : ViewModelBase
{
    public Role Role { get; }

    /// <summary>Invoked when IsSelected flips, so the parent VM can re-evaluate CanCreate.</summary>
    private readonly Action? _onSelectionChanged;

    public RoleSelectionItem(Role role, Action? onSelectionChanged = null)
    {
        Role = role;
        _onSelectionChanged = onSelectionChanged;
    }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke();

    public string Avatar => Role.Avatar;
    public string Name => Role.Name;
    public string Description => Role.Description;
}

/// <summary>View-model for the "create group chat" dialog: pick a title + ≥2 roles.</summary>
public partial class CreateGroupChatViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly INavigation _navigation;
    private readonly ILogger<CreateGroupChatViewModel> _logger;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public ObservableCollection<RoleSelectionItem> Roles { get; } = new();

    /// <summary>Set by the window; invoking it closes the dialog with DialogResult=true.</summary>
    public Action? RequestClose { get; set; }

    public CreateGroupChatViewModel(IRoleService roleService, INavigation navigation, ILogger<CreateGroupChatViewModel> logger)
    {
        _roleService = roleService;
        _navigation = navigation;
        _logger = logger;
    }

    public bool CanCreate => Roles.Any(r => r.IsSelected) && Roles.Count(r => r.IsSelected) >= 2;

    partial void OnTitleChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    /// <summary>Re-evaluate CanCreate when a checkbox toggles.</summary>
    internal void NotifySelectionChanged() => CreateCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        Roles.Clear();
        try
        {
            var roles = await _roleService.GetAllAsync();
            foreach (var r in roles) Roles.Add(new RoleSelectionItem(r, NotifySelectionChanged));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roles for group-chat dialog.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ErrorText = string.Empty;
        var selected = Roles.Where(r => r.IsSelected).Select(r => r.Role).ToList();
        if (selected.Count < 2)
        {
            ErrorText = "群聊至少需要选择 2 个角色。";
            return;
        }
        try
        {
            RequestClose?.Invoke();
            await _navigation.OpenNewGroupChatAsync(selected, Title?.Trim() ?? string.Empty);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.LogError(ex, "Create group chat failed.");
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
