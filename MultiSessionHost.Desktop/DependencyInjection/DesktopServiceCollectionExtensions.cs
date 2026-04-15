using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Drivers;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Processes;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Windows;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Services;

namespace MultiSessionHost.Desktop.DependencyInjection;

public static class DesktopServiceCollectionExtensions
{
    public const string DesktopTargetHttpClientName = "MultiSessionHost.Desktop.Targets";

    public static IServiceCollection AddDesktopSessionServices(this IServiceCollection services)
    {
        services.AddHttpClient(
            DesktopTargetHttpClientName,
            static client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });

        services.AddSingleton<IProcessLocator, Win32ProcessLocator>();
        services.AddSingleton<IWindowLocator, Win32WindowLocator>();
        services.AddSingleton<IDesktopTargetProfileResolver, ConfiguredDesktopTargetProfileResolver>();
        services.AddSingleton<IDesktopTargetMatcher, DefaultDesktopTargetMatcher>();
        services.AddSingleton<ISessionAttachmentResolver, DefaultSessionAttachmentResolver>();
        services.AddSingleton<IAttachedSessionStore, InMemoryAttachedSessionStore>();
        services.AddSingleton<IUiSnapshotSerializer, JsonUiSnapshotSerializer>();
        services.AddSingleton<IUiSnapshotProvider, SelfHostedHttpUiSnapshotProvider>();
        services.AddSingleton<SelfHostedHttpUiTreeNormalizer>();
        services.AddSingleton<TestAppUiTreeNormalizer>();
        services.AddSingleton<IUiTreeNormalizerResolver, DefaultUiTreeNormalizerResolver>();
        services.AddSingleton<DefaultButtonWorkItemPlanner>();
        services.AddSingleton<TestAppWorkItemPlanner>();
        services.AddSingleton<IWorkItemPlannerResolver, DefaultWorkItemPlannerResolver>();
        services.AddSingleton<IUiNodeSelector, DefaultUiNodeSelector>();
        services.AddSingleton<IUiStateProjector, DefaultUiStateProjector>();
        services.AddSingleton<IDesktopTargetAdapter, SelfHostedHttpDesktopTargetAdapter>();
        services.AddSingleton<IDesktopTargetAdapter, DesktopTestAppTargetAdapter>();
        services.AddSingleton<IDesktopTargetAdapterRegistry, DesktopTargetAdapterRegistry>();
        services.AddSingleton<DesktopTargetSessionDriver>();

        return services;
    }
}
