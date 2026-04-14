using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Coordination;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task InitializeAsync_RegistersSessionsAndAllowsQueries()
    {
        var context = new TestRuntimeContext(
            TestOptionsFactory.Create(
                TestOptionsFactory.Session("alpha"),
                TestOptionsFactory.Session("beta", enabled: false)),
            new FakeClock(new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)),
            new TestSessionDriver());

        await context.InitializeAsync();

        var sessions = context.Coordinator.GetSessions().OrderBy(session => session.SessionId.Value).ToArray();
        var beta = context.Coordinator.GetSession(new SessionId("beta"));

        Assert.Equal(2, sessions.Length);
        Assert.NotNull(beta);
        Assert.Equal(SessionStatus.Created, beta!.Runtime.CurrentStatus);
        Assert.Equal(SessionStatus.Stopped, beta.Runtime.DesiredStatus);
    }

    [Fact]
    public async Task HeartbeatWorkItem_UpdatesHeartbeatAndMetrics()
    {
        var context = new TestRuntimeContext(
            TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")),
            new FakeClock(new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero)),
            new TestSessionDriver());

        await context.InitializeAsync();
        await context.Coordinator.StartSessionAsync(new SessionId("alpha"), CancellationToken.None);
        await context.LifecycleManager.EnqueueAsync(
            new SessionId("alpha"),
            SessionWorkItem.Create(new SessionId("alpha"), SessionWorkItemKind.Heartbeat, context.Clock.UtcNow, "test heartbeat"),
            CancellationToken.None);

        await TestWait.UntilAsync(
            () => context.Coordinator.GetProcessHealth().TotalHeartbeatsEmitted == 1,
            TimeSpan.FromSeconds(2),
            "Heartbeat was not emitted in time.");

        var state = await context.GetStateAsync("alpha");
        var health = context.Coordinator.GetProcessHealth();

        Assert.NotNull(state.LastHeartbeatUtc);
        Assert.Equal(1, health.TotalHeartbeatsEmitted);

        await context.Coordinator.ShutdownAsync(CancellationToken.None);
    }
}
