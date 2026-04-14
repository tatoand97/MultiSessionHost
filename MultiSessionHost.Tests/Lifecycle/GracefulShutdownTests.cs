using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Lifecycle;

public sealed class GracefulShutdownTests
{
    [Fact]
    public async Task ShutdownAsync_DrainsQueuedWorkAndStopsSession()
    {
        var driver = new TestSessionDriver(workDelay: TimeSpan.FromMilliseconds(50));
        var context = new TestRuntimeContext(
            TestOptionsFactory.Create(TestOptionsFactory.Session("alpha", maxParallelWorkItems: 1)),
            new FakeClock(new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)),
            driver);

        await context.InitializeAsync();
        await context.Coordinator.StartSessionAsync(new SessionId("alpha"), CancellationToken.None);

        await context.LifecycleManager.EnqueueAsync(
            new SessionId("alpha"),
            SessionWorkItem.Create(new SessionId("alpha"), SessionWorkItemKind.Tick, context.Clock.UtcNow, "tick-1"),
            CancellationToken.None);

        await context.LifecycleManager.EnqueueAsync(
            new SessionId("alpha"),
            SessionWorkItem.Create(new SessionId("alpha"), SessionWorkItemKind.Tick, context.Clock.UtcNow, "tick-2"),
            CancellationToken.None);

        await context.Coordinator.ShutdownAsync(CancellationToken.None);

        var state = await context.GetStateAsync("alpha");

        Assert.Equal(2, driver.Executions[new SessionId("alpha")]);
        Assert.Equal(SessionStatus.Stopped, state.CurrentStatus);
    }
}
