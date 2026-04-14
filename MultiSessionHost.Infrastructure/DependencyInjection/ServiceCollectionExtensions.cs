using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Drivers;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Infrastructure.Drivers;
using MultiSessionHost.Infrastructure.Health;
using MultiSessionHost.Infrastructure.Lifecycle;
using MultiSessionHost.Infrastructure.Queues;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Infrastructure.Scheduling;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Infrastructure.Time;

namespace MultiSessionHost.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMultiSessionHostRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISessionRegistry, InMemorySessionRegistry>();
        services.AddSingleton<ISessionStateStore, InMemorySessionStateStore>();
        services.AddSingleton<ISessionUiStateStore, InMemorySessionUiStateStore>();
        services.AddSingleton<IWorkQueue, ChannelBasedWorkQueue>();
        services.AddSingleton<IHealthReporter, DefaultHealthReporter>();
        services.AddSingleton<ISessionScheduler, RoundRobinSessionScheduler>();
        services.AddSingleton<NoOpSessionDriver>();
        services.AddSingleton<MockDesktopSessionAdapter>();
        services.AddDesktopSessionServices();
        services.AddSingleton<ISessionDriver>(
            static serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<SessionHostOptions>();

                return options.DriverMode switch
                {
                    DriverMode.NoOp => serviceProvider.GetRequiredService<NoOpSessionDriver>(),
                    DriverMode.DesktopTestApp => serviceProvider.GetRequiredService<DesktopTestAppSessionDriver>(),
                    _ => throw new InvalidOperationException($"DriverMode '{options.DriverMode}' is not supported.")
                };
            });
        services.AddSingleton<ISessionLifecycleManager, DefaultSessionLifecycleManager>();
        services.AddSingleton<ISessionCoordinator, DefaultSessionCoordinator>();

        return services;
    }
}
