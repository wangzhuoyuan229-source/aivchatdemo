using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.ViewModels;

/// <summary>A single memory row in the management panel.</summary>
public partial class MemoryItemViewModel : ViewModelBase
{
    public MemoryEntry Entry { get; }
    public string SourceRoleText { get; }

    public MemoryItemViewModel(MemoryEntry entry, string sourceRoleName)
    {
        Entry = entry;
        SourceRoleText = $"来源角色 · {sourceRoleName}";
    }

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
/// Shared long-term memory panel: list / add / edit / delete / clear.
/// Every memory records the role whose conversation triggered it.
/// </summary>
public partial class MemoryManagementViewModel : ViewModelBase
{
    private readonly IMemoryService _memory;
    private readonly IRoleService _roles;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MemoryManagementViewModel> _logger;

    public ObservableCollection<MemoryItemViewModel> Items { get; } = new();

    /// <summary>Selectable source roles for manually added memories.</summary>
    public ObservableCollection<Role> SelectableRoles { get; } = new();

    [ObservableProperty] private Role? _selectedRole;
    [ObservableProperty] private string _headerText = "共享长期记忆";
    [ObservableProperty] private string _newMemoryText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool HasSelectableRoles => SelectableRoles.Count > 1;
    public bool CanAdd => SelectedRole is not null;

    public MemoryManagementViewModel(
        IMemoryService memory,
        IRoleService roles,
        IDialogService dialogs,
        ILogger<MemoryManagementViewModel> logger)
    {
        _memory = memory;
        _roles = roles;
        _dialogs = dialogs;
        _logger = logger;
    }

    partial void OnSelectedRoleChanged(Role? value)
    {
        AddCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Private-chat mode: manual memories default to the current role as their source.</summary>
    public async Task InitializeAsync(Role role)
    {
        SelectableRoles.Clear();
        SelectableRoles.Add(role);
        SelectedRole = role;
        OnPropertyChanged(nameof(HasSelectableRoles));
        await LoadAsync();
    }

    /// <summary>Group-chat mode: the selected member is recorded as the source of a manual memory.</summary>
    public async Task InitializeGroupAsync(IReadOnlyList<Role> members)
    {
        SelectableRoles.Clear();
        foreach (var member in members)
            SelectableRoles.Add(member);
        OnPropertyChanged(nameof(HasSelectableRoles));
        SelectedRole = SelectableRoles.FirstOrDefault();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _memory.ListAllAsync();
            var roleNames = (await _roles.GetAllAsync()).ToDictionary(role => role.Id, role => role.Name);
            Items.Clear();
            foreach (var entry in entries)
            {
                var sourceRoleName = roleNames.GetValueOrDefault(entry.RoleId, $"角色 #{entry.RoleId}");
                Items.Add(new MemoryItemViewModel(entry, sourceRoleName));
            }
            StatusText = Items.Count == 0
                ? "暂无共享记忆。任一角色对话中的自动抽取都会出现在这里。"
                : $"共 {Items.Count} 条共享记忆，所有角色均可召回";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load shared memories failed.");
            StatusText = $"加载失败：{SafeError(ex)}";
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
            StatusText = $"新增失败：{SafeError(ex)}";
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
            StatusText = $"保存失败：{SafeError(ex)}";
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
            StatusText = $"删除失败：{SafeError(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task ClearAsync()
    {
        if (!await _dialogs.ConfirmAsync(
                "确定要清空全部共享记忆吗？\n\n所有角色都将无法再召回这些记忆，该操作不可撤销。",
                "清空共享记忆"))
            return;
        IsBusy = true;
        try
        {
            await _memory.ClearAllAsync();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"清空失败：{SafeError(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
