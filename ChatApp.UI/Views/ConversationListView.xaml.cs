using System.Windows.Controls;
using System.Windows.Input;

namespace ChatApp.UI.Views;

public partial class ConversationListView : UserControl
{
    public ConversationListView()
    {
        InitializeComponent();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewModels.ConversationListViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }
}
