using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChatApp.UI.ViewModels;

namespace ChatApp.UI.Views;

public partial class ChatView : UserControl
{
    private INotifyCollectionChanged? _currentSource;
    private bool _deferCurrentEnterToIme;

    public ChatView()
    {
        AvaloniaXamlLoader.Load(this);
        var inputBox = this.FindControl<TextBox>("InputBox");
        inputBox?.AddHandler(
            InputElement.KeyUpEvent,
            InputBox_KeyUp,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        Loaded += ChatView_Loaded;
    }

    private async void ChatView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChatViewModel vm && vm.Conversation is not null)
            await vm.RefreshCurrentAsync();
        ScrollToBottom();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentSource is not null)
            _currentSource.CollectionChanged -= OnMessagesChanged;
        if (DataContext is ChatViewModel vm)
        {
            _currentSource = vm.Messages;
            _currentSource.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToBottom();

    private void ScrollToBottom() => Dispatcher.UIThread.Post(() =>
        this.FindControl<ScrollViewer>("MessagesScroll")?.ScrollToEnd(), DispatcherPriority.Background);

    private void InputBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is ChatViewModel vm && sender is TextBox tb)
            vm.UpdateMentionState(tb.Text ?? string.Empty, tb.CaretIndex);
    }

    private void MentionList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ChatViewModel vm && sender is ListBox lb && lb.SelectedItem is ChatApp.Core.Models.Role role)
        {
            vm.InsertMention(role);
            var tb = this.FindControl<TextBox>("InputBox");
            if (tb != null)
            {
                tb.CaretIndex = vm.InputText.Length;
                tb.Focus();
            }
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ChatInputKeyPolicy.IsPlainEnter(e.Key, e.KeyModifiers))
        {
            _deferCurrentEnterToIme = sender is TextBox inputBox && HasActiveImePreedit(inputBox);
            if (_deferCurrentEnterToIme)
            {
                // The IME owns this Enter: it confirms the highlighted candidate.
                // Do not select an @ mention or turn the resulting edit into Send.
                return;
            }
        }
        else
        {
            _deferCurrentEnterToIme = false;
        }

        if (DataContext is ChatViewModel vm && vm.IsMentionPopupOpen)
        {
            if (e.Key == Key.Down)
            {
                if (vm.FilteredMentionCandidates.Count > 0)
                    vm.SelectedMentionIndex = (vm.SelectedMentionIndex + 1) % vm.FilteredMentionCandidates.Count;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (vm.FilteredMentionCandidates.Count > 0)
                    vm.SelectedMentionIndex = (vm.SelectedMentionIndex - 1 + vm.FilteredMentionCandidates.Count) % vm.FilteredMentionCandidates.Count;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                vm.IsMentionPopupOpen = false;
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.Enter || e.Key == Key.Return) && e.KeyModifiers == KeyModifiers.None)
            {
                if (vm.FilteredMentionCandidates.Count > 0 && vm.SelectedMentionIndex >= 0 && vm.SelectedMentionIndex < vm.FilteredMentionCandidates.Count)
                {
                    var role = vm.FilteredMentionCandidates[vm.SelectedMentionIndex];
                    vm.InsertMention(role);
                    var tb2 = this.FindControl<TextBox>("InputBox");
                    if (tb2 != null)
                    {
                        tb2.CaretIndex = vm.InputText.Length;
                        tb2.Focus();
                    }
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void InputBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (!ChatInputKeyPolicy.IsPlainEnter(e.Key, e.KeyModifiers))
            return;

        var deferredToIme = _deferCurrentEnterToIme;
        _deferCurrentEnterToIme = false;
        if (deferredToIme ||
            sender is not TextBox inputBox || DataContext is not ChatViewModel vm)
            return;

        // KeyUp runs after TextBox and the platform IME. A committed Chinese
        // candidate leaves a Chinese character before the caret; a normal Enter
        // leaves a line break, which is the only case converted into Send.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, vm)) return;
            if (HasActiveImePreedit(inputBox)) return;
            var currentText = inputBox.Text ?? string.Empty;
            if (!ChatInputKeyPolicy.TryRemoveLineBreakBeforeCaret(
                    currentText,
                    inputBox.CaretIndex,
                    out var textToSend,
                    out var caretIndex))
                return;

            inputBox.Text = textToSend;
            inputBox.CaretIndex = caretIndex;
            vm.InputText = textToSend;
            if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
        }, DispatcherPriority.Background);
    }

    private static bool HasActiveImePreedit(TextBox inputBox) =>
        inputBox.GetVisualDescendants()
            .OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatViewModel vm || vm.Conversation is null) return;
        var choice = await DialogExportChoice.ShowAsync(this);
        if (choice is null) return;
        await vm.ExportAsync(choice.Value ? "json" : "md");
    }

    /// <summary>Tiny in-place export-format picker (null = cancelled).</summary>
    private sealed class DialogExportChoice
    {
        public static async Task<bool?> ShowAsync(Control owner)
        {
            var topLevel = TopLevel.GetTopLevel(owner) as Window;
            if (topLevel is null) return null;

            var dialog = new Window
            {
                Title = "导出会话",
                Width = 300,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Background = Avalonia.Application.Current!.FindResource("PanelBrush") as Avalonia.Media.IBrush
            };
            var result = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var md = new Button { Content = "导出为 Markdown（.md）", Classes = { "primary" }, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
            var json = new Button { Content = "导出为 JSON（.json）", Classes = { "primary" }, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
            var cancel = new Button { Content = "取消", Classes = { "ghost" } };
            md.Click += (_, _) => { result.TrySetResult(false); dialog.Close(); };
            json.Click += (_, _) => { result.TrySetResult(true); dialog.Close(); };
            cancel.Click += (_, _) => { result.TrySetResult(null); dialog.Close(); };
            dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(20), Children = { md, json, cancel } };
            dialog.Closing += (_, _) => result.TrySetResult(null);
            await dialog.ShowDialog(topLevel);
            return result.Task.IsCompleted ? result.Task.Result : null;
        }
    }
}
