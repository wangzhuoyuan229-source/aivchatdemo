namespace ChatApp.UI.Services;

public enum DeleteGroupChoice
{
    DeleteDocuments,
    KeepDocuments
}

public interface IDialogService
{
    Task<string?> PickFileAsync();
    Task<IReadOnlyList<string>> PickFilesAsync();
    Task<string?> PickFolderAsync();
    Task<IReadOnlyList<string>> PickFoldersAsync();
    Task<bool> ConfirmAsync(string message, string title);
    Task ShowErrorAsync(string message, string title = "错误");
    Task<(bool confirmed, string text)> PromptAsync(string prompt, string defaultValue = "", string title = "输入");
    Task<(bool confirmed, int? value)> SelectAsync(string prompt, IEnumerable<(string label, int? value)> options, string title = "选择");
    Task<DeleteGroupChoice?> ConfirmDeleteGroupAsync(string message, string title);
}
