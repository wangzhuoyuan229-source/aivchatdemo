using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ChatApp.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.UI.Views;

public partial class RoleListView : UserControl
{
    public RoleListView() => AvaloniaXamlLoader.Load(this);

    private async void CreateRole_Click(object? sender, RoutedEventArgs e)
    {
        var vm = App.Services.GetRequiredService<CreateRoleViewModel>();
        await vm.PrepareForCreateAsync();

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        var window = new CreateRoleWindow { DataContext = vm };
        vm.RequestClose = () => window.Close(true);
        await window.ShowDialog<bool>(owner);
        if (DataContext is RoleListViewModel list) await list.LoadAsync();
    }

    private async void EditRole_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatApp.Core.Models.Role role }) return;

        var vm = App.Services.GetRequiredService<CreateRoleViewModel>();
        await vm.PrepareForEditAsync(role);

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        var window = new CreateRoleWindow { DataContext = vm };
        vm.RequestClose = () => window.Close(true);
        await window.ShowDialog<bool>(owner);
        if (DataContext is RoleListViewModel list) await list.LoadAsync();
    }

    private async void CreateGroupChat_Click(object? sender, RoutedEventArgs e)
    {
        var vm = App.Services.GetRequiredService<CreateGroupChatViewModel>();
        vm.Title = string.Empty;
        vm.ErrorText = string.Empty;
        await vm.LoadAsync();

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        var window = new CreateGroupChatWindow { DataContext = vm };
        vm.RequestClose = () => window.Close(true);
        await window.ShowDialog<bool>(owner);
    }
}
