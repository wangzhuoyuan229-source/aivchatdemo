using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ChatApp.UI.Views;

public partial class InputDialog : Window
{
    public string PromptText { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;

    public InputDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += (_, _) =>
        {
            var input = this.FindControl<TextBox>("InputBox")!;
            input.Focus();
            input.SelectAll();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    private void Ok_Click(object? sender, RoutedEventArgs e) => Confirm();

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            this.FindControl<TextBox>("InputBox")!.Focus();
            return;
        }
        Close(true);
    }
}
