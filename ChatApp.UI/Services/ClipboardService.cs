using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace ChatApp.UI.Services;

/// <summary>Clipboard access through the main window's TopLevel.</summary>
public static class ClipboardService
{
    public static async Task CopyTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null) return;
        var clipboard = desktop.MainWindow.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }
}

public static class FileSaveService
{
    public static async Task<string?> PickSavePathAsync(string suggestedName, string patternTitle, string pattern)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null) return null;
        var provider = desktop.MainWindow.StorageProvider;
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出会话",
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(patternTitle) { Patterns = [pattern] }
            ]
        });
        return file?.TryGetLocalPath();
    }
}
