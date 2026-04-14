using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Lifecycle;

public sealed class SessionIsolationTests
{
    [Fact]
    public async Task FaultedSession_DoesNotStopHealthySessions()
    {
        var driver = new TestSessionDriver(
            shouldFail: static (snapshot, _) => snapshot.SessionId.Value == "alpha");

        var context = new TestRuntimeContext(
            TestOptionsFactory.Create(
                TestOptionsFactory.Session("alpha", maxRetryCount: 0),
                TestOptionsFactory.Session("beta", maxRetryCount: 3)),
            new FakeClock(new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)),
            driver);

        await context.InitializeAsync();
        await context.Coordinator.StartSessionAsync(new SessionId("alpha"), CancellationToken.None);
        await context.Coordinator.StartSessionAsync(new SessionId("beta"), CancellationToken.None);

        await context.LifecycleManager.EnqueueAsync(
            new SessionId("alpha"),
            SessionWorkItem.Create(new SessionId("alpha"), SessionWorkItemKind.Tick, context.Clock.UtcNow, "fault alpha"),
            CancellationToken.None);

        await context.LifecycleManager.EnqueueAsync(
            new SessionId("beta"),
            SessionWorkItem.Create(new SessionId("beta"), SessionWorkItemKind.Tick, context.Clock.UtcNow, "healthy beta"),
            CancellationToken.None);

        await TestWait.UntilAsync(
            () => context.Coordinator.GetSession(new SessionId("alpha"))?.Runtime.CurrentStatus == SessionStatus.Faulted,
            TimeSpan.FromSeconds(2),
            "Alpha session did not reach Faulted state.");

        await TestWait.UntilAsync(
            () => context.Coordinator.GetProcessHealth().TotalTicksExecuted == 1,
            TimeSpan.FromSeconds(2),
            "Beta session did not execute its tick.");

        var alphaState = await context.GetStateAsync("alpha");
        var betaState = await context.GetStateAsync("beta");

        Assert.Equal(SessionStatus.Faulted, alphaState.CurrentStatus);
        Assert.Equal(SessionStatus.Running, betaState.CurrentStatus);

        await context.Coordinator.ShutdownAsync(CancellationToken.None);
    }
}
