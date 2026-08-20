using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>A single memory row in the management panel.</summary>
public partial class MemoryItemViewModel : ViewModelBase
{
    public MemoryEntry Entry { get; }

    public MemoryItemViewModel(MemoryEntry entry) => Entry = entry;

    public int Id => Entry.Id;
    public string Content => Entry.Content;
    public string CreatedText => Entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _draft = string.Empty;

    public void BeginEdit()
    {
        Draft = Entry.Content;
        IsEditing = true;
    }
}

/// <summary>
/// Role-scoped long-term memory panel: list / add / edit / delete / clear.
/// In group chats the member selector switches which role's memory is shown;
/// only that role's memory is ever visible (isolation constraint).
/// </summary>
public partial class MemoryManagementViewModel : ViewModelBase
{
    private readonly IMemoryService _memory;
    private readonly ILogger<MemoryManagementViewModel> _logger;

    public ObservableCollection<MemoryItemViewModel> Items { get; } = new();

    /// <summary>Selectable roles (group mode); empty in private mode.</summary>
    public ObservableCollection<Role> SelectableRoles { get; } = new();

    [ObservableProperty] private Role? _selectedRole;
    [ObservableProperty] private string _headerText = string.Empty;
    [ObservableProperty] private string _newMemoryText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool HasSelectableRoles => SelectableRoles.Count > 1;
    public bool CanAdd => SelectedRole is not null;

    public MemoryManagementViewModel(IMemoryService memory, ILogger<MemoryManagementViewModel> logger)
    {
        _memory = memory;
        _logger = logger;
    }

    partial void OnSelectedRoleChanged(Role? value)
    {
        HeaderText = value is null ? string.Empty : $"「{value.Name}」的长期记忆";
        AddCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        _ = LoadAsync();
    }

    /// <summary>Private-chat mode: the panel shows the single bound role.</summary>
    public Task InitializeAsync(Role role)
    {
        SelectableRoles.Clear();
        SelectableRoles.Add(role);
        SelectedRole = role;
        return Task.CompletedTask;
    }

    /// <summary>Group-chat mode: members are selectable so each speaker's memory stays isolated.</summary>
    public async Task InitializeGroupAsync(IReadOnlyList<Role> members)
    {
        SelectableRoles.Clear();
        foreach (var member in members)
            SelectableRoles.Add(member);
        OnPropertyChanged(nameof(HasSelectableRoles));
        SelectedRole = SelectableRoles.FirstOrDefault();
        await Task.CompletedTask;
    }

    public async Task LoadAsync()
    {
        var role = SelectedRole;
        if (role is null) return;
        IsBusy = true;
        try
        {
            var entries = await _memory.ListAsync(role.Id);
            Items.Clear();
            foreach (var entry in entries) Items.Add(new MemoryItemViewModel(entry));
            StatusText = Items.Count == 0 ? "暂无记忆。对话中的自动抽取会出现在这里。" : $"共 {Items.Count} 条记忆";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load memories failed for role {Id}.", role.Id);
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var role = SelectedRole;
        var content = NewMemoryText.Trim();
        if (role is null || content.Length == 0) return;
        IsBusy = true;
        try
        {
            await _memory.RememberAsync(role.Id, null, content);
            NewMemoryText = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"新增失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveEditAsync(MemoryItemViewModel item)
    {
        var content = item.Draft.Trim();
        if (content.Length == 0)
        {
            StatusText = "记忆内容不能为空。";
            return;
        }
        IsBusy = true;
        try
        {
            await _memory.UpdateAsync(item.Id, content);
            item.IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEdit(MemoryItemViewModel item) => item.IsEditing = false;

    [RelayCommand]
    private async Task EditAsync(MemoryItemViewModel item)
    {
        foreach (var other in Items.Where(i => i.IsEditing && !ReferenceEquals(i, item)))
            other.IsEditing = false;
        item.BeginEdit();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteAsync(MemoryItemViewModel item)
    {
        IsBusy = true;
        try
        {
            await _memory.ForgetAsync(item.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"删除失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task ClearAsync()
    {
        var role = SelectedRole;
        if (role is null) return;
        IsBusy = true;
        try
        {
            await _memory.ClearForRoleAsync(role.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"清空失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
