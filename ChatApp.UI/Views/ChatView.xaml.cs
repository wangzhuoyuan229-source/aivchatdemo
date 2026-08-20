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

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None &&
            DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
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
