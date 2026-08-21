using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ChatApp.UI.ViewModels;

namespace ChatApp.UI.Views;

public partial class ChatView : UserControl
{
    private INotifyCollectionChanged? _currentSource;

    public ChatView()
    {
        AvaloniaXamlLoader.Load(this);
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
        var isEnter = e.Key == Key.Enter || e.Key == Key.Return;
        if (isEnter && e.KeyModifiers == KeyModifiers.None &&
            DataContext is ChatViewModel vm2 && vm2.SendCommand.CanExecute(null))
        {
            // If mention popup is open, Enter should have been handled above
            vm2.SendCommand.Execute(null);
            e.Handled = true;
        }
        // Shift+Enter 保持换行（AcceptsReturn=true 时默认行为），此处不拦截
    }

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
