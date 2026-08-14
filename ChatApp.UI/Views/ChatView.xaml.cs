using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatApp.UI.ViewModels;

namespace ChatApp.UI.Views;

public partial class ChatView : UserControl
{
    private INotifyCollectionChanged? _currentSource;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += ChatView_Loaded;
    }

    private async void ChatView_Loaded(object sender, RoutedEventArgs e)
    {
        // 当 ChatView 被重新加载（从 null 切回 Chat）时，强制重新加载消息以触发 UI 更新
        if (DataContext is ChatViewModel vm && vm.Conversation is not null)
        {
            // 切回时主动重新加载消息，确保 UI 正确显示历史对话
            await vm.RefreshCurrentAsync();
            // 滚动到底部显示最新消息
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MessagesScroll.ScrollableHeight > 0)
                    MessagesScroll.ScrollToBottom();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_currentSource is not null)
            _currentSource.CollectionChanged -= OnMessagesChanged;

        if (e.NewValue is ChatViewModel vm)
        {
            _currentSource = vm.Messages;
            vm.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Scroll to bottom whenever messages change (added during streaming too).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MessagesScroll.ScrollableHeight > 0)
                MessagesScroll.ScrollToBottom();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
