using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Interfaces;
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
        services.AddSingleton<IWorkQueue, ChannelBasedWorkQueue>();
        services.AddSingleton<IHealthReporter, DefaultHealthReporter>();
        services.AddSingleton<ISessionScheduler, RoundRobinSessionScheduler>();
        services.AddSingleton<NoOpSessionDriver>();
        services.AddSingleton<MockDesktopSessionAdapter>();
        services.AddSingleton<ISessionDriver>(static serviceProvider => serviceProvider.GetRequiredService<NoOpSessionDriver>());
        services.AddSingleton<ISessionLifecycleManager, DefaultSessionLifecycleManager>();
        services.AddSingleton<ISessionCoordinator, DefaultSessionCoordinator>();

        return services;
    }
}
