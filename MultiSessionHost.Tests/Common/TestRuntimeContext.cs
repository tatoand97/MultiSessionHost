using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Infrastructure.Health;
using MultiSessionHost.Infrastructure.Lifecycle;
using MultiSessionHost.Infrastructure.Queues;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Infrastructure.Scheduling;
using MultiSessionHost.Infrastructure.State;

namespace MultiSessionHost.Tests.Common;

public sealed class TestRuntimeContext
{
    public TestRuntimeContext(SessionHostOptions options, FakeClock clock, TestSessionDriver driver)
    {
        Options = options;
        Clock = clock;
        Driver = driver;
        Registry = new InMemorySessionRegistry();
        StateStore = new InMemorySessionStateStore();
        WorkQueue = new ChannelBasedWorkQueue();
        HealthReporter = new DefaultHealthReporter();
        Scheduler = new RoundRobinSessionScheduler();
        LifecycleManager = new DefaultSessionLifecycleManager(
            Registry,
            StateStore,
            WorkQueue,
            Driver,
            Clock,
            HealthReporter,
            NullLogger<DefaultSessionLifecycleManager>.Instance);
        Coordinator = new DefaultSessionCoordinator(
            Options,
            Registry,
            StateStore,
            Scheduler,
            LifecycleManager,
            WorkQueue,
            Clock,
            HealthReporter,
            NullLogger<DefaultSessionCoordinator>.Instance);
    }

    public SessionHostOptions Options { get; }

    public FakeClock Clock { get; }

    public TestSessionDriver Driver { get; }

    public InMemorySessionRegistry Registry { get; }

    public InMemorySessionStateStore StateStore { get; }

    public ChannelBasedWorkQueue WorkQueue { get; }

    public DefaultHealthReporter HealthReporter { get; }

    public RoundRobinSessionScheduler Scheduler { get; }

    public DefaultSessionLifecycleManager LifecycleManager { get; }

    public DefaultSessionCoordinator Coordinator { get; }

    public async Task InitializeAsync()
    {
        await Coordinator.InitializeAsync(CancellationToken.None);
    }

    public async Task<SessionRuntimeState> GetStateAsync(string sessionId)
    {
        return await StateStore.GetAsync(new SessionId(sessionId), CancellationToken.None)
            ?? throw new InvalidOperationException($"State '{sessionId}' was not found.");
    }
}
