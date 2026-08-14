using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ChatApp.UI.ViewModels;

namespace ChatApp.UI.Views;

public partial class ConversationListView : UserControl
{
    public ConversationListView() => AvaloniaXamlLoader.Load(this);

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ConversationListViewModel vm)
            vm.SearchCommand.Execute(null);
    }

    private void Conversations_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ConversationItemViewModel item } &&
            DataContext is ConversationListViewModel vm && vm.OpenCommand.CanExecute(item))
            vm.OpenCommand.Execute(item);
    }
}
