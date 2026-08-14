using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChatApp.AI;
using ChatApp.Infrastructure;
using ChatApp.UI.Services;
using ChatApp.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
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
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<INavigation>(sp => sp.GetRequiredService<MainViewModel>());
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<RoleListViewModel>();
                services.AddSingleton<ConversationListViewModel>();
                services.AddSingleton<KnowledgeViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<CreateRoleViewModel>();
                services.AddSingleton<CreateGroupChatViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
        Services = _host.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = _host.Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = window;
            window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            window.Show();

            try
            {
                await InfrastructureModule.InitializeAsync(_host.Services);
                await ((MainViewModel)window.DataContext).InitializeAsync();
            }
            catch (Exception ex)
            {
                await _host.Services.GetRequiredService<IDialogService>()
                    .ShowErrorAsync($"初始化数据库失败：\n{ex.Message}", "启动错误");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
