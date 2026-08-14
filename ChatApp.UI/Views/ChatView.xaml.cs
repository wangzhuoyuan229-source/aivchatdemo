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
}
