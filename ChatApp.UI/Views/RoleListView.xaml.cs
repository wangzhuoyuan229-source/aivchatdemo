using System.Windows;
using System.Windows.Controls;
using ChatApp.UI.ViewModels;
using ChatApp.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.UI.Views;

public partial class RoleListView : UserControl
{
    public RoleListView()
    {
        InitializeComponent();
    }

    private void CreateRole_Click(object sender, RoutedEventArgs e)
    {
        var vm = App.Services.GetRequiredService<CreateRoleViewModel>();
        // Reset fields for a fresh creation.
        vm.Name = string.Empty;
        vm.Avatar = "🎭";
        vm.Description = string.Empty;
        vm.Background = string.Empty;
        vm.Personality = string.Empty;
        vm.SpeakingStyle = string.Empty;
        vm.Greeting = string.Empty;
        vm.ErrorText = string.Empty;

        var window = new CreateRoleWindow { DataContext = vm, Owner = Window.GetWindow(this) };
        vm.RequestClose = () => window.DialogResult = true;
        window.ShowDialog();

        // The list will be refreshed when navigation returns to roles; force a refresh too.
        if (DataContext is RoleListViewModel list)
        {
            list.RefreshCommand.Execute(null);
        }
    }

    private async void CreateGroupChat_Click(object sender, RoutedEventArgs e)
    {
        var vm = App.Services.GetRequiredService<CreateGroupChatViewModel>();
        vm.Title = string.Empty;
        vm.ErrorText = string.Empty;
        await vm.LoadAsync();

        var window = new CreateGroupChatWindow { DataContext = vm, Owner = Window.GetWindow(this) };
        vm.RequestClose = () => window.DialogResult = true;
        window.ShowDialog();
    }
}
