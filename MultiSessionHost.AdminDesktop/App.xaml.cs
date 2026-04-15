using System.Windows;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MultiSessionHost.AdminDesktop.Api;
using MultiSessionHost.AdminDesktop.Services;
using MultiSessionHost.AdminDesktop.ViewModels;

namespace MultiSessionHost.AdminDesktop;

public partial class App : Application
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices(
                services =>
                {
                    services.AddSingleton(new HttpClient());
                    services.AddSingleton<IAdminApiClient, AdminApiClient>();
                    services.AddSingleton<IRefreshCoordinator, RefreshCoordinator>();
                    services.AddSingleton<ShellViewModel>();
                    services.AddSingleton<MainWindow>();
                })
            .Build();

        await host.StartAsync().ConfigureAwait(true);

        var window = host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync().ConfigureAwait(true);
            host.Dispose();
        }

        base.OnExit(e);
    }
}
