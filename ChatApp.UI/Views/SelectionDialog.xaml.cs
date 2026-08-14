using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ChatApp.UI.Views;

public partial class SelectionDialog : Window
{
    public string PromptText { get; set; } = string.Empty;

    public sealed class Option
    {
        public string Label { get; set; } = string.Empty;
        public int? Value { get; set; }
    }

    public ObservableCollection<Option> Options { get; } = new();
    public int? SelectedValue => this.FindControl<ComboBox>("SelectionBox")?.SelectedItem is Option option ? option.Value : null;

    public SelectionDialog()
    {
        InitializeComponent();
        DataContext = this;
        Opened += (_, _) =>
        {
            var box = this.FindControl<ComboBox>("SelectionBox")!;
            if (box.ItemCount > 0) box.SelectedIndex = 0;
            box.Focus();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
