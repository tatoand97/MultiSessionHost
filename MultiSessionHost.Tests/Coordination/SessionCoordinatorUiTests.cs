using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Extensions;

namespace MultiSessionHost.Tests.Coordination;

public sealed class SessionCoordinatorUiTests
{
    [Fact]
    public async Task RefreshSessionUiAsync_StoresProjectedTreeAndKeepsSessionsIsolated()
    {
        const int basePort = 7710;
        const string alphaId = "coord-ui-alpha";
        const string betaId = "coord-ui-beta";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);

        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            TestOptionsFactory.Session(alphaId, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var alphaSessionId = new SessionId(alphaId);
        var betaSessionId = new SessionId(betaId);

        await WaitForRunningAsync(harness, alphaSessionId, betaSessionId);

        var alphaUi = await harness.Coordinator.RefreshSessionUiAsync(alphaSessionId, CancellationToken.None);
        var betaUi = await harness.Coordinator.RefreshSessionUiAsync(betaSessionId, CancellationToken.None);
        var attachments = harness.Host.Services.GetRequiredService<IAttachedSessionStore>().GetAll().ToDictionary(static item => item.SessionId);

        Assert.NotNull(alphaUi.ProjectedTree);
        Assert.NotNull(betaUi.ProjectedTree);
        Assert.NotNull(alphaUi.RawSnapshotJson);
        Assert.NotNull(betaUi.RawSnapshotJson);
        Assert.Equal(alphaApp.ProcessId, attachments[alphaSessionId].Process.ProcessId);
        Assert.Equal(betaApp.ProcessId, attachments[betaSessionId].Process.ProcessId);
        Assert.Contains(alphaId, alphaUi.ProjectedTree!.Flatten().Select(static node => node.Text).Where(static text => text is not null)!);
        Assert.Contains(betaId, betaUi.ProjectedTree!.Flatten().Select(static node => node.Text).Where(static text => text is not null)!);
        Assert.DoesNotContain(betaId, alphaUi.ProjectedTree.Flatten().Select(static node => node.Text).Where(static text => text is not null)!);
        Assert.DoesNotContain(alphaId, betaUi.ProjectedTree.Flatten().Select(static node => node.Text).Where(static text => text is not null)!);
    }

    private static async Task WaitForRunningAsync(WorkerHostHarness harness, params SessionId[] sessionIds)
    {
        await TestWait.UntilAsync(
            () => sessionIds.All(sessionId => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running),
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed sessions in time.");
    }
}
