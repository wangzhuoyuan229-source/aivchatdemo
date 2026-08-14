using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ChatApp.UI.ViewModels;

public partial class KnowledgeViewModel : ViewModelBase
{
    private readonly IKnowledgeService _knowledge;
    private readonly ILogger<KnowledgeViewModel> _logger;

    public ObservableCollection<GroupNode> Groups { get; } = new();
    public ObservableCollection<SelectableDocument> Documents { get; } = new();

    /// <summary>当前选中的分组节点。GroupId=-1 表示「全部」。</summary>
    [ObservableProperty] private GroupNode? _selectedGroup;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isImporting;

    /// <summary>当前选中的文档数量（用于显示和按钮可用性）。</summary>
    [ObservableProperty] private int _selectedCount;

    /// <summary>全选复选框状态。true=全选, false=未全选, null=部分选中。绑定到 IsThreeState CheckBox。</summary>
    [ObservableProperty] private bool? _isAllSelected = false;

    /// <summary>是否处于批量管理模式。</summary>
    [ObservableProperty] private bool _isBatchMode;

    /// <summary>导入文档时使用的分组 Id（null=未分组）。受 SelectedGroup 影响。</summary>
    private int? CurrentImportGroupId => SelectedGroup switch
    {
        null => null,
        { GroupId: -1 } => null,
        { GroupId: null } => null,
        { GroupId: var id } => id,
    };

    public KnowledgeViewModel(IKnowledgeService knowledge, ILogger<KnowledgeViewModel> logger)
    {
        _knowledge = knowledge;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        try
        {
            var groups = await _knowledge.ListGroupsAsync();
            var allDocs = await _knowledge.ListDocumentsAsync();

            Groups.Clear();
            Groups.Add(new GroupNode { DisplayName = "全部", GroupId = -1, DocumentCount = allDocs.Count });
            Groups.Add(new GroupNode { DisplayName = "未分组", GroupId = null, DocumentCount = allDocs.Count(d => d.GroupId == null) });
            foreach (var g in groups)
                Groups.Add(new GroupNode
                {
                    DisplayName = g.Name,
                    GroupId = g.Id,
                    SourceGroup = g,
                    DocumentCount = allDocs.Count(d => d.GroupId == g.Id)
                });

            if (SelectedGroup is null)
                SelectedGroup = Groups[0];
            else
            {
                var prev = SelectedGroup;
                SelectedGroup = prev.GroupId switch
                {
                    -1 => Groups[0],
                    null => Groups.FirstOrDefault(x => x.GroupId == null) ?? Groups[0],
                    var id => Groups.FirstOrDefault(x => x.GroupId == id) ?? Groups[0]
                };
            }

            await RefreshDocumentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge view.");
        }
    }

    /// <summary>根据当前选中分组刷新右侧文档列表。</summary>
    private async Task RefreshDocumentsAsync()
    {
        // 先取消所有旧的订阅
        foreach (var d in Documents) d.PropertyChanged -= OnDocumentSelectionChanged;
        Documents.Clear();
        try
        {
            IReadOnlyList<KnowledgeDocument> docs = SelectedGroup switch
            {
                null => await _knowledge.ListDocumentsAsync(),
                { GroupId: -1 } => await _knowledge.ListDocumentsAsync(),
                { GroupId: null } => await _knowledge.ListDocumentsByGroupAsync(null),
                { GroupId: var id } => await _knowledge.ListDocumentsByGroupAsync(id),
            };
            foreach (var d in docs)
            {
                var sd = new SelectableDocument(d);
                sd.PropertyChanged += OnDocumentSelectionChanged;
                Documents.Add(sd);
            }
            UpdateSelectionState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh documents.");
        }
    }

    /// <summary>单个文档选中状态变化时，更新 SelectedCount 和 IsAllSelected。</summary>
    private void OnDocumentSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableDocument.IsSelected))
            UpdateSelectionState();
    }

    /// <summary>重新计算选中数量和全选状态。</summary>
    private void UpdateSelectionState()
    {
        SelectedCount = Documents.Count(d => d.IsSelected);
        if (Documents.Count == 0)
            IsAllSelected = false;
        else if (SelectedCount == Documents.Count)
            IsAllSelected = true;
        else if (SelectedCount == 0)
            IsAllSelected = false;
        else
            IsAllSelected = null; // 部分选中
    }

    /// <summary>全选/取消全选时，同步所有文档的 IsSelected。</summary>
    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (!value.HasValue) return; // null 状态不主动改
        foreach (var d in Documents) d.IsSelected = value.Value;
        SelectedCount = value.Value ? Documents.Count : 0;
    }

    partial void OnSelectedGroupChanged(GroupNode? value) => _ = RefreshDocumentsAsync();

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private void ToggleBatchMode()
    {
        IsBatchMode = !IsBatchMode;
        if (!IsBatchMode)
        {
            foreach (var d in Documents) d.IsSelected = false;
            UpdateSelectionState();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var d in Documents) d.IsSelected = true;
        UpdateSelectionState();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var d in Documents) d.IsSelected = false;
        UpdateSelectionState();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择知识库文档",
            Filter = "支持的文档|*.txt;*.md;*.pdf|文本文件|*.txt|Markdown|*.md|PDF|*.pdf|所有文件|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        IsImporting = true;
        ProgressText = "导入中…";
        try
        {
            var targetGroup = CurrentImportGroupId;
            var progress = new Progress<(int done, int total)>(p => ProgressText = p.total > 0 ? $"分块并嵌入 {p.done}/{p.total}" : "处理中…");
            await _knowledge.ImportAsync(dlg.FileName, progress, groupId: targetGroup);
            await LoadAsync();
            ProgressText = $"导入完成 ✓（已加入：{SelectedGroup?.DisplayName ?? "未分组"}）";
        }
        catch (Exception ex)
        {
            ProgressText = $"失败：{ex.Message}";
            _logger.LogError(ex, "Import failed.");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var dlg = new OpenFolderDialog { Title = "选择知识库文件夹（含子文件夹）" };
        if (dlg.ShowDialog() != true) return;

        IsImporting = true;
        ProgressText = "扫描文件夹…";
        try
        {
            var targetGroup = CurrentImportGroupId;
            var progress = new Progress<(int doneFiles, int totalFiles, string currentFile)>(p =>
            {
                if (p.totalFiles <= 0) ProgressText = "扫描中…";
                else if (string.IsNullOrEmpty(p.currentFile)) ProgressText = $"完成 {p.doneFiles}/{p.totalFiles}";
                else ProgressText = $"导入 {p.doneFiles + 1}/{p.totalFiles}：{p.currentFile}";
            });
            var docs = await _knowledge.ImportDirectoryAsync(dlg.FolderName, recursive: true, progress, groupId: targetGroup);
            await LoadAsync();
            ProgressText = docs.Count > 0 ? $"文件夹导入完成 ✓（{docs.Count} 个文档）" : "未发现可导入的文档";
        }
        catch (Exception ex)
        {
            ProgressText = $"失败：{ex.Message}";
            _logger.LogError(ex, "Import folder failed.");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(SelectableDocument doc)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"确定要删除文档「{doc.Title}」吗？\n\n该操作将一并删除其所有分块与向量。",
            "删除文档", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _knowledge.DeleteDocumentAsync(doc.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete document failed.");
            System.Windows.MessageBox.Show($"删除失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>把单个文档移动到指定分组。弹出选择对话框。</summary>
    [RelayCommand]
    private async Task MoveDocumentAsync(SelectableDocument doc)
    {
        var groups = await _knowledge.ListGroupsAsync();
        var options = new List<(string label, int? value)> { ("未分组", null) };
        foreach (var g in groups)
            options.Add((g.Name, (int?)g.Id));

        int? targetGroupId = null;
        bool ok = false;
        try
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var (confirmed, value) = SelectionDialog.Show(
                    $"选择「{doc.Title}」的目标分组：", options, title: "移动文档");
                ok = confirmed;
                targetGroupId = value;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show SelectionDialog failed.");
            System.Windows.MessageBox.Show($"打开对话框失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        if (!ok) return;

        try
        {
            await _knowledge.MoveDocumentAsync(doc.Id, targetGroupId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"移动文档失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.LogWarning(ex, "Move document failed.");
        }
    }

    // ----- 批量操作 -----

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var selected = Documents.Where(d => d.IsSelected).Select(d => d.Id).ToList();
        if (selected.Count == 0) return;

        var confirm = System.Windows.MessageBox.Show(
            $"确定要删除选中的 {selected.Count} 个文档吗？\n\n该操作将一并删除这些文档的所有分块与向量，不可撤销。",
            "批量删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _knowledge.DeleteDocumentsAsync(selected);
            await LoadAsync();
            ProgressText = $"已删除 {selected.Count} 个文档";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch delete failed.");
            System.Windows.MessageBox.Show($"批量删除失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task BatchMoveAsync()
    {
        var selectedIds = Documents.Where(d => d.IsSelected).Select(d => d.Id).ToList();
        if (selectedIds.Count == 0) return;

        var groups = await _knowledge.ListGroupsAsync();
        var options = new List<(string label, int? value)> { ("未分组", null) };
        foreach (var g in groups)
            options.Add((g.Name, (int?)g.Id));

        int? targetGroupId = null;
        bool ok = false;
        try
        {
            // 确保在 UI 线程上弹出对话框
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var (confirmed, value) = SelectionDialog.Show(
                    $"选择 {selectedIds.Count} 个文档的目标分组：", options, title: "批量移动文档");
                ok = confirmed;
                targetGroupId = value;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show SelectionDialog failed.");
            System.Windows.MessageBox.Show($"打开对话框失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        if (!ok) return;

        try
        {
            await _knowledge.MoveDocumentsAsync(selectedIds, targetGroupId);
            await LoadAsync();
            ProgressText = $"已移动 {selectedIds.Count} 个文档";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"批量移动失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.LogWarning(ex, "Batch move failed.");
        }
    }

    // ----- 分组管理 -----

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        var (ok, name) = InputDialog.Show("请输入分组名称：", defaultValue: "", title: "新建分组");
        if (!ok || string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _knowledge.CreateGroupAsync(name);
            await LoadAsync();
            var created = Groups.FirstOrDefault(g => g.DisplayName == name.Trim() && g.GroupId > 0);
            if (created is not null) SelectedGroup = created;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"创建分组失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.LogWarning(ex, "Create group failed.");
        }
    }

    [RelayCommand]
    private async Task RenameGroupAsync(GroupNode node)
    {
        if (node?.SourceGroup is null) return;
        var (ok, name) = InputDialog.Show("请输入新的分组名称：", defaultValue: node.DisplayName, title: "重命名分组");
        if (!ok || string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _knowledge.RenameGroupAsync(node.SourceGroup.Id, name);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"重命名失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.LogWarning(ex, "Rename group failed.");
        }
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(GroupNode node)
    {
        if (node?.SourceGroup is null) return;

        var msg = $"确定要删除分组「{node.DisplayName}」吗？\n\n该分组下有 {node.DocumentCount} 个文档。\n\n" +
                  "「是」：一并删除这些文档（含向量）\n" +
                  "「否」：把这些文档移到「未分组」（保留文档）\n" +
                  "「取消」：放弃操作";
        var result = System.Windows.MessageBox.Show(msg, "删除分组",
            System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);

        bool deleteDocuments;
        switch (result)
        {
            case System.Windows.MessageBoxResult.Yes: deleteDocuments = true; break;
            case System.Windows.MessageBoxResult.No: deleteDocuments = false; break;
            default: return;
        }

        try
        {
            await _knowledge.DeleteGroupAsync(node.SourceGroup.Id, deleteDocuments);
            SelectedGroup = Groups[0];
            await LoadAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"删除分组失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _logger.LogWarning(ex, "Delete group failed.");
        }
    }
}
