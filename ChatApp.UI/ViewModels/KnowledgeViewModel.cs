using System.Collections.ObjectModel;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ChatApp.UI.ViewModels;

public partial class KnowledgeViewModel : ViewModelBase
{
    private readonly IKnowledgeService _knowledge;
    private readonly BundledKnowledgeService _bundledKnowledge;
    private readonly ILogger<KnowledgeViewModel> _logger;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _importCts;
    private bool _suppressGroupRefresh;

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

    public KnowledgeViewModel(
        IKnowledgeService knowledge,
        ILogger<KnowledgeViewModel> logger,
        IDialogService dialogs,
        BundledKnowledgeService bundledKnowledge)
    {
        _knowledge = knowledge;
        _logger = logger;
        _dialogs = dialogs;
        _bundledKnowledge = bundledKnowledge;
    }

    public async Task LoadAsync()
    {
        try
        {
            var previousSelectionKey = SelectedGroup?.SelectionKey;
            var groups = await _knowledge.ListGroupsAsync();
            var allDocs = await _knowledge.ListDocumentsAsync();

            _suppressGroupRefresh = true;
            Groups.Clear();
            foreach (var node in KnowledgeFolderTree.Build(groups, allDocs))
                Groups.Add(node);
            SelectedGroup = Groups.FirstOrDefault(node => node.SelectionKey == previousSelectionKey) ?? Groups.FirstOrDefault();
            _suppressGroupRefresh = false;

            await RefreshDocumentsAsync();
        }
        catch (Exception ex)
        {
            _suppressGroupRefresh = false;
            _logger.LogError(ex, "Failed to load knowledge view.");
        }
    }

    /// <summary>
    /// Starts/resumes the one-time indexing of the corpus shipped with the app.
    /// Safe to call after every startup or settings save: the persistent marker and
    /// per-path resume logic prevent duplicate vectors and repeated paid API calls.
    /// </summary>
    public async Task ImportBundledKnowledgeAsync()
    {
        if (IsImporting || !_bundledKnowledge.IsPackaged) return;

        IsImporting = true;
        ProgressText = "正在检查内置知识库…";
        _importCts = new CancellationTokenSource();
        try
        {
            var importTimer = Stopwatch.StartNew();
            var progress = new Progress<KnowledgeImportProgress>(p =>
            {
                ProgressText = "内置知识库 · " + FormatImportProgress(p, importTimer.Elapsed);
            });
            var result = await _bundledKnowledge.EnsureImportedAsync(progress, _importCts.Token);
            switch (result.Status)
            {
                case BundledKnowledgeImportStatus.Imported:
                    await LoadAsync();
                    ProgressText = $"内置知识库就绪（共 {result.Total} 项，新增 {result.Imported}，" +
                                   $"复用 {result.Skipped}，从未分组迁移 {result.MovedFromUngrouped}）；" +
                                   "已放入“内置知识库”分组，可在角色设置中绑定。";
                    break;
                case BundledKnowledgeImportStatus.Partial:
                    await LoadAsync();
                    ProgressText = $"内置知识库本次新增 {result.Imported}，仍有 {result.Failed} 项未完成；" +
                                   $"下次启动会自动续传。{result.Detail}";
                    break;
                case BundledKnowledgeImportStatus.Deferred:
                    if (result.GroupId.HasValue) await LoadAsync();
                    ProgressText = result.Detail;
                    break;
                case BundledKnowledgeImportStatus.AlreadyCurrent:
                    ProgressText = result.Detail;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await LoadAsync();
            ProgressText = "内置知识库索引已取消；已完成项保留，下次启动会自动续传。";
        }
        catch (Exception ex)
        {
            ProgressText = $"内置知识库索引失败：{ex.Message}；下次启动会自动重试。";
            _logger.LogError(ex, "Built-in knowledge import failed.");
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
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
            if (SelectedGroup is { IsFolder: true } folder)
                docs = docs.Where(document => KnowledgeFolderTree.Contains(document, folder.FolderPath)).ToList();
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

    partial void OnSelectedGroupChanged(GroupNode? value)
    {
        if (!_suppressGroupRefresh) _ = RefreshDocumentsAsync();
    }

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
        if (IsImporting) return;
        var filePaths = await _dialogs.PickFilesAsync();
        if (filePaths.Count == 0) return;

        IsImporting = true;
        ProgressText = "导入中…";
        _importCts = new CancellationTokenSource();
        try
        {
            var targetGroup = CurrentImportGroupId;
            KnowledgeImportProgress? latestProgress = null;
            var importTimer = Stopwatch.StartNew();
            var progress = new Progress<KnowledgeImportProgress>(p =>
            {
                latestProgress = p;
                ProgressText = FormatImportProgress(p, importTimer.Elapsed);
            });
            var documents = await _knowledge.ImportFilesAsync(filePaths, progress, _importCts.Token, targetGroup);
            await LoadAsync();
            var total = latestProgress?.Total ?? filePaths.Count;
            var failed = latestProgress?.Failed ?? Math.Max(0, total - documents.Count);
            var skipped = latestProgress?.SkippedCount ?? 0;
            ProgressText = $"导入完成（新增 {documents.Count}，跳过 {skipped}，失败 {failed}，已加入：{SelectedGroup?.DisplayName ?? "未分组"}）";
            if (failed > 0)
                await ShowImportFailureSummaryAsync(documents.Count, failed, latestProgress?.LastError);
        }
        catch (OperationCanceledException)
        {
            await LoadAsync();
            ProgressText = "导入已取消；已完成的知识项已保留。";
        }
        catch (Exception ex)
        {
            ProgressText = $"失败：{ex.Message}";
            _logger.LogError(ex, "Import failed.");
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (IsImporting) return;
        var folderPaths = await _dialogs.PickFoldersAsync();
        if (folderPaths.Count == 0) return;

        IsImporting = true;
        ProgressText = "扫描文件夹…";
        _importCts = new CancellationTokenSource();
        try
        {
            var targetGroup = CurrentImportGroupId;
            KnowledgeImportProgress? latestProgress = null;
            var importTimer = Stopwatch.StartNew();
            var progress = new Progress<KnowledgeImportProgress>(p =>
            {
                latestProgress = p;
                ProgressText = FormatImportProgress(p, importTimer.Elapsed);
            });
            var docs = await _knowledge.ImportDirectoriesAsync(
                folderPaths,
                recursive: true,
                progress: progress,
                ct: _importCts.Token,
                groupId: targetGroup);
            await LoadAsync();
            var failed = latestProgress?.Failed ?? 0;
            var skipped = latestProgress?.SkippedCount ?? 0;
            ProgressText = docs.Count > 0 || skipped > 0 || failed > 0
                ? $"文件夹批量导入完成（{folderPaths.Count} 个根目录，新增 {docs.Count}，跳过 {skipped}，失败 {failed}，目录层级已保留）"
                : "所选文件夹中未发现可导入的文件";
            if (failed > 0)
                await ShowImportFailureSummaryAsync(docs.Count, failed, latestProgress?.LastError);
        }
        catch (OperationCanceledException)
        {
            await LoadAsync();
            ProgressText = "导入已取消；已完成的知识项已保留。";
        }
        catch (Exception ex)
        {
            ProgressText = $"失败：{ex.Message}";
            _logger.LogError(ex, "Import folder failed.");
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private void CancelImport() => _importCts?.Cancel();

    [RelayCommand]
    private async Task EditImageMetadataAsync(SelectableDocument doc)
    {
        if (!doc.IsImage) return;
        var (descriptionOk, description) = await _dialogs.PromptAsync(
            "编辑用于检索和后续对话指代的图片描述：", doc.Description, "编辑图片描述");
        if (!descriptionOk || string.IsNullOrWhiteSpace(description)) return;
        var (tagsOk, tags) = await _dialogs.PromptAsync(
            "编辑标签（使用逗号分隔）：", doc.Tags, "编辑图片标签");
        if (!tagsOk) return;

        try
        {
            ProgressText = "正在重新生成图片向量索引…";
            await _knowledge.UpdateImageMetadataAsync(doc.Id, description, tags);
            await RefreshDocumentsAsync();
            ProgressText = "图片描述与标签已更新 ✓";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"更新图片语义失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RegenerateImageDescriptionAsync(SelectableDocument doc)
    {
        if (!doc.IsImage) return;
        try
        {
            ProgressText = $"正在使用当前多模态模型识别「{doc.Title}」…";
            await _knowledge.RegenerateImageDescriptionAsync(doc.Id);
            await RefreshDocumentsAsync();
            ProgressText = "重新识图并索引完成 ✓";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"重新识图失败：{ex.Message}");
            ProgressText = $"重新识图失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenImageAsync(SelectableDocument doc)
    {
        if (!doc.IsImage || string.IsNullOrWhiteSpace(doc.PreviewPath) || !File.Exists(doc.PreviewPath))
        {
            await _dialogs.ShowErrorAsync("图片原文件缺失或已损坏。", "无法打开图片");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(doc.PreviewPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"无法打开图片：{ex.Message}");
        }
    }

    private static string FormatImportStage(KnowledgeImportStage stage) => stage switch
    {
        KnowledgeImportStage.Scanning => "扫描",
        KnowledgeImportStage.Copying => "复制原图",
        KnowledgeImportStage.Describing => "识别图片",
        KnowledgeImportStage.Embedding => "生成向量",
        KnowledgeImportStage.Persisting => "保存",
        _ => "完成"
    };

    private static string FormatImportProgress(KnowledgeImportProgress progress, TimeSpan elapsed)
    {
        var current = string.IsNullOrWhiteSpace(progress.CurrentFile)
            ? string.Empty
            : $"：{progress.CurrentFile}";
        var error = string.IsNullOrWhiteSpace(progress.LastError)
            ? string.Empty
            : $"；最近错误：{progress.LastError}";
        var work = string.Empty;
        if (progress.TotalBytes > 0)
        {
            var ratio = Math.Clamp((double)progress.ProcessedBytes / progress.TotalBytes, 0, 1);
            var eta = ratio > 0.005
                ? TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - ratio) / ratio)
                : TimeSpan.Zero;
            var etaText = eta > TimeSpan.Zero ? $"，预计剩余 {FormatDuration(eta)}" : string.Empty;
            work = $"，数据 {ratio:P1}{etaText}";
        }
        return $"{FormatImportStage(progress.Stage)} {progress.Completed}/{progress.Total}{work}，" +
               $"新增 {progress.Succeeded}，跳过 {progress.SkippedCount}，失败 {progress.Failed}，" +
               $"回退 {progress.FallbackCount}{current}{error}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分钟";
        if (duration.TotalMinutes >= 1)
            return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes))} 分钟";
        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} 秒";
    }

    private Task ShowImportFailureSummaryAsync(int succeeded, int failed, string? lastError)
    {
        var detail = string.IsNullOrWhiteSpace(lastError) ? "请检查 Embedding 配置和网络连接。" : lastError;
        var title = succeeded == 0 ? "知识库导入失败" : "部分知识文件导入失败";
        return _dialogs.ShowErrorAsync($"成功 {succeeded} 项，失败 {failed} 项。\n\n最近错误：{detail}", title);
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(SelectableDocument doc)
    {
        var confirm = await _dialogs.ConfirmAsync(
            $"确定要删除文档「{doc.Title}」吗？\n\n该操作将一并删除其所有分块与向量。",
            "删除文档");
        if (!confirm) return;

        try
        {
            await _knowledge.DeleteDocumentAsync(doc.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete document failed.");
            await _dialogs.ShowErrorAsync($"删除失败：{ex.Message}");
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

        int? targetGroupId;
        bool ok;
        try
        {
            (ok, targetGroupId) = await _dialogs.SelectAsync(
                $"选择「{doc.Title}」的目标分组：", options, "移动文档");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show SelectionDialog failed.");
            await _dialogs.ShowErrorAsync($"打开对话框失败：{ex.Message}");
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
            await _dialogs.ShowErrorAsync($"移动文档失败：{ex.Message}");
            _logger.LogWarning(ex, "Move document failed.");
        }
    }

    // ----- 批量操作 -----

    [RelayCommand]
    private async Task BatchRegenerateImagesAsync()
    {
        if (IsImporting) return;
        var selected = Documents.Where(document => document.IsSelected && document.IsImage)
            .Select(document => document.Id)
            .ToList();
        if (selected.Count == 0)
        {
            await _dialogs.ShowErrorAsync("请先选择至少一张图片知识项。", "批量重新识图");
            return;
        }
        var confirmed = await _dialogs.ConfirmAsync(
            $"将使用当前多模态 API 重新识别并索引 {selected.Count} 张图片，是否继续？",
            "批量重新识图");
        if (!confirmed) return;

        IsImporting = true;
        ProgressText = "正在批量识图…";
        _importCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<KnowledgeImportProgress>(p =>
            {
                ProgressText = $"批量识图 {p.Completed}/{p.Total}，成功 {p.Succeeded}，失败 {p.Failed}，回退 {p.FallbackCount}";
            });
            var regenerated = await _knowledge.RegenerateImageDescriptionsAsync(selected, progress, _importCts.Token);
            await LoadAsync();
            ProgressText = $"批量识图完成 ✓（成功 {regenerated.Count}，失败 {selected.Count - regenerated.Count}）";
        }
        catch (OperationCanceledException)
        {
            await LoadAsync();
            ProgressText = "批量识图已取消；已完成项已保留。";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch image recognition failed.");
            ProgressText = $"批量识图失败：{ex.Message}";
            await _dialogs.ShowErrorAsync($"批量识图失败：{ex.Message}");
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var selected = Documents.Where(d => d.IsSelected).Select(d => d.Id).ToList();
        if (selected.Count == 0) return;

        var confirm = await _dialogs.ConfirmAsync(
            $"确定要删除选中的 {selected.Count} 个文档吗？\n\n该操作将一并删除这些文档的所有分块与向量，不可撤销。",
            "批量删除");
        if (!confirm) return;

        try
        {
            await _knowledge.DeleteDocumentsAsync(selected);
            await LoadAsync();
            ProgressText = $"已删除 {selected.Count} 个文档";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch delete failed.");
            await _dialogs.ShowErrorAsync($"批量删除失败：{ex.Message}");
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

        int? targetGroupId;
        bool ok;
        try
        {
            (ok, targetGroupId) = await _dialogs.SelectAsync(
                $"选择 {selectedIds.Count} 个文档的目标分组：", options, "批量移动文档");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show SelectionDialog failed.");
            await _dialogs.ShowErrorAsync($"打开对话框失败：{ex.Message}");
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
            await _dialogs.ShowErrorAsync($"批量移动失败：{ex.Message}");
            _logger.LogWarning(ex, "Batch move failed.");
        }
    }

    // ----- 分组管理 -----

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        var (ok, name) = await _dialogs.PromptAsync("请输入分组名称：", "", "新建分组");
        if (!ok || string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _knowledge.CreateGroupAsync(name);
            await LoadAsync();
            var created = Groups.FirstOrDefault(g => g.DisplayName == name.Trim() && g.SourceGroup is not null);
            if (created is not null) SelectedGroup = created;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"创建分组失败：{ex.Message}");
            _logger.LogWarning(ex, "Create group failed.");
        }
    }

    [RelayCommand]
    private async Task RenameGroupAsync(GroupNode node)
    {
        if (node?.SourceGroup is null) return;
        var (ok, name) = await _dialogs.PromptAsync("请输入新的分组名称：", node.DisplayName, "重命名分组");
        if (!ok || string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _knowledge.RenameGroupAsync(node.SourceGroup.Id, name);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"重命名失败：{ex.Message}");
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
        var result = await _dialogs.ConfirmDeleteGroupAsync(msg, "删除分组");
        if (result is null) return;
        var deleteDocuments = result == DeleteGroupChoice.DeleteDocuments;

        try
        {
            await _knowledge.DeleteGroupAsync(node.SourceGroup.Id, deleteDocuments);
            SelectedGroup = Groups[0];
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync($"删除分组失败：{ex.Message}");
            _logger.LogWarning(ex, "Delete group failed.");
        }
    }
}
