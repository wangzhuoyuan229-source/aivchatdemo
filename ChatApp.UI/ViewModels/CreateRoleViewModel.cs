using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>P2: view-model for the create-custom-role dialog.</summary>
public partial class CreateRoleViewModel : ViewModelBase
{
    private readonly IRoleService _roleService;
    private readonly INavigation _navigation;
    private readonly ILogger<CreateRoleViewModel> _logger;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _avatar = "🎭";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _background = string.Empty;
    [ObservableProperty] private string _personality = string.Empty;
    [ObservableProperty] private string _speakingStyle = string.Empty;
    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    public Action? RequestClose { get; set; }

    public CreateRoleViewModel(IRoleService roleService, INavigation navigation, ILogger<CreateRoleViewModel> logger)
    {
        _roleService = roleService;
        _navigation = navigation;
        _logger = logger;
    }

    public bool CanCreate => !string.IsNullOrWhiteSpace(Name);

    partial void OnNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ErrorText = string.Empty;
        if (!CanCreate) return;
        try
        {
            var role = new Role
            {
                Name = Name.Trim(),
                Avatar = string.IsNullOrWhiteSpace(Avatar) ? "🎭" : Avatar.Trim(),
                Description = Description,
                Background = Background,
                Personality = Personality,
                SpeakingStyle = SpeakingStyle,
                Greeting = Greeting,
                IsPreset = false
            };
            role = await _roleService.CreateAsync(role);
            RequestClose?.Invoke();
            await _navigation.OpenChatForRoleAsync(role);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            _logger.LogError(ex, "Create role failed.");
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
