using System.Windows;
using System.Windows.Input;

namespace ChatApp.UI.Views;

public partial class InputDialog : Window
{
    public string PromptText { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;
    public string ConfirmText { get; set; } = "确定";

    public bool IsConfirmed { get; private set; }

    public InputDialog()
    {
        InitializeComponent();
        InputBox.DataContext = this;
        DataContext = this;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            InputBox.Focus();
            return;
        }
        IsConfirmed = true;
        DialogResult = true;
        Close();
    }

    /// <summary>打开输入对话框，返回是否确认及输入的文本。</summary>
    public static (bool confirmed, string text) Show(string prompt, string defaultValue = "", string title = "输入")
    {
        var dlg = new InputDialog { PromptText = prompt, InputText = defaultValue, Title = title };
        dlg.OkButtonSet();
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
            dlg.Owner = owner;
        var result = dlg.ShowDialog();
        return (result == true, dlg.InputText);
    }

    private void OkButtonSet()
    {
        // 由 ConfirmText 决定按钮文本，目前默认「确定」
    }
}
