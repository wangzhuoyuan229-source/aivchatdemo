using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ChatApp.UI.Views;

public enum MessageDialogResult
{
    Cancel,
    Primary,
    Secondary
}

public partial class MessageDialog : Window
{
    public string MessageText { get; }
    public string PrimaryText { get; }
    public string? SecondaryText { get; }
    public string? CancelText { get; }

    public MessageDialog() : this(string.Empty, string.Empty, "确定") { }

    public MessageDialog(string message, string title, string primary, string? secondary = null, string? cancel = null)
    {
        MessageText = message;
        PrimaryText = primary;
        SecondaryText = secondary;
        CancelText = cancel;
        Title = title;
        DataContext = this;
        InitializeComponent();
        this.FindControl<Button>("SecondaryButton")!.IsVisible = secondary is not null;
        this.FindControl<Button>("CancelButton")!.IsVisible = cancel is not null || secondary is not null;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    private void Primary_Click(object? sender, RoutedEventArgs e) => Close(MessageDialogResult.Primary);
    private void Secondary_Click(object? sender, RoutedEventArgs e) => Close(MessageDialogResult.Secondary);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(MessageDialogResult.Cancel);
}
