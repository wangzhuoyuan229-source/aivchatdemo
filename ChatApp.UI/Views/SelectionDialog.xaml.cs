using System.Collections.ObjectModel;
using System.Windows;

namespace ChatApp.UI.Views;

public partial class SelectionDialog : Window
{
    public string PromptText { get; set; } = string.Empty;

    public class Option
    {
        public string Label { get; set; } = string.Empty;
        public int? Value { get; set; }
    }

    public ObservableCollection<Option> Options { get; } = new();

    public SelectionDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            if (SelectionBox.Items.Count > 0)
                SelectionBox.SelectedIndex = 0;
            SelectionBox.Focus();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm()
    {
        DialogResult = true;
        Close();
    }

    /// <summary>打开选择对话框，返回是否确认及选中的值。</summary>
    public static (bool confirmed, int? value) Show(string prompt, IEnumerable<(string label, int? value)> options, string title = "选择")
    {
        var dlg = new SelectionDialog { PromptText = prompt, Title = title };
        foreach (var (label, value) in options)
            dlg.Options.Add(new Option { Label = label, Value = value });

        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
            dlg.Owner = owner;

        var result = dlg.ShowDialog();
        if (result != true) return (false, null);

        var idx = dlg.SelectionBox.SelectedIndex;
        if (idx < 0 || idx >= dlg.Options.Count) return (true, null);
        return (true, dlg.Options[idx].Value);
    }
}
