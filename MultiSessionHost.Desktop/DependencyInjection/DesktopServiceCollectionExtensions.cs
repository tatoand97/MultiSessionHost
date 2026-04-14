using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Drivers;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Processes;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Windows;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Services;

namespace MultiSessionHost.Desktop.DependencyInjection;

public static class DesktopServiceCollectionExtensions
{
    public const string TestAppHttpClientName = "MultiSessionHost.Desktop.TestDesktopApp";

    public static IServiceCollection AddDesktopSessionServices(this IServiceCollection services)
    {
        services.AddHttpClient(
            TestAppHttpClientName,
            static client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });

        services.AddSingleton<IProcessLocator, Win32ProcessLocator>();
        services.AddSingleton<IWindowLocator, Win32WindowLocator>();
        services.AddSingleton<ISessionAttachmentResolver, DefaultSessionAttachmentResolver>();
        services.AddSingleton<IAttachedSessionStore, InMemoryAttachedSessionStore>();
        services.AddSingleton<IUiSnapshotSerializer, JsonUiSnapshotSerializer>();
        services.AddSingleton<IUiSnapshotProvider, TestAppUiSnapshotProvider>();
        services.AddSingleton<IUiTreeNormalizer, TestAppUiTreeNormalizer>();
        services.AddSingleton<IWorkItemPlanner, TestAppWorkItemPlanner>();
        services.AddSingleton<IUiNodeSelector, DefaultUiNodeSelector>();
        services.AddSingleton<IUiStateProjector, DefaultUiStateProjector>();
        services.AddSingleton<DesktopTestAppSessionDriver>();

        return services;
    }
}
