using System.Windows;
using ChatApp.AI;
using ChatApp.Infrastructure;
using ChatApp.UI.ViewModels;
using ChatApp.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI;

public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>Global service provider for view code-behind that needs DI (e.g. dialogs).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
            })
            .ConfigureServices((_, services) =>
            {
                services.AddInfrastructure();
                services.AddChatAppAi();

                // View models
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<INavigation>(sp => sp.GetRequiredService<MainViewModel>());
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<RoleListViewModel>();
                services.AddSingleton<ConversationListViewModel>();
                services.AddSingleton<KnowledgeViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<CreateRoleViewModel>();
                services.AddSingleton<CreateGroupChatViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
        Services = _host.Services;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await InfrastructureModule.InitializeAsync(_host.Services);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化数据库失败：\n{ex.Message}", "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        var vm = _host.Services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        await vm.InitializeAsync();
        window.Show();
    }
}
