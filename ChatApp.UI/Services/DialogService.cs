using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ChatApp.UI.Views;

namespace ChatApp.UI.Services;

public sealed class DialogService : IDialogService
{
    private static Window Owner =>
        ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow
        ?? throw new InvalidOperationException("主窗口尚未创建。");

    public async Task<string?> PickFileAsync()
    {
        var files = await PickFilesAsync();
        return files.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择知识库文档或图片",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("支持的知识文件") { Patterns = ["*.txt", "*.md", "*.markdown", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.webp"] },
                new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
                FilePickerFileTypes.All
            ]
        });
        return files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await PickFoldersAsync();
        return folders.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync()
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择一个或多个知识库文件夹（含子文件夹）",
            AllowMultiple = true
        });
        return folders.Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }

    public async Task<bool> ConfirmAsync(string message, string title)
    {
        var dialog = new MessageDialog(message, title, "确定", "取消");
        return await dialog.ShowDialog<MessageDialogResult>(Owner) == MessageDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string message, string title = "错误")
    {
        var dialog = new MessageDialog(message, title, "确定");
        await dialog.ShowDialog<MessageDialogResult>(Owner);
    }

    public async Task<(bool confirmed, string text)> PromptAsync(string prompt, string defaultValue = "", string title = "输入")
    {
        var dialog = new InputDialog { PromptText = prompt, InputText = defaultValue, Title = title };
        var confirmed = await dialog.ShowDialog<bool>(Owner);
        return (confirmed, dialog.InputText);
    }

    public async Task<(bool confirmed, int? value)> SelectAsync(
        string prompt, IEnumerable<(string label, int? value)> options, string title = "选择")
    {
        var dialog = new SelectionDialog { PromptText = prompt, Title = title };
        foreach (var (label, value) in options)
            dialog.Options.Add(new SelectionDialog.Option { Label = label, Value = value });
        var confirmed = await dialog.ShowDialog<bool>(Owner);
        return (confirmed, confirmed ? dialog.SelectedValue : null);
    }

    public async Task<DeleteGroupChoice?> ConfirmDeleteGroupAsync(string message, string title)
    {
        var dialog = new MessageDialog(message, title, "删除文档", "保留文档", "取消");
        return await dialog.ShowDialog<MessageDialogResult>(Owner) switch
        {
            MessageDialogResult.Primary => DeleteGroupChoice.DeleteDocuments,
            MessageDialogResult.Secondary => DeleteGroupChoice.KeepDocuments,
            _ => null
        };
    }
}
