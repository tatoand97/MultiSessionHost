using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Processes;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Windows;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class SessionAttachmentResolverTests
{
    [Fact]
    public async Task ResolveAsync_FindsTheCorrectInstanceBySessionId()
    {
        const int basePort = 7510;
        const string alphaId = "attach-alpha";
        const string betaId = "attach-beta";

        await using var alpha = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var beta = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);

        var options = CreateDesktopOptions(basePort, alphaId, betaId);
        var resolver = new DefaultSessionAttachmentResolver(
            new ConfiguredDesktopTargetProfileResolver(options),
            new Win32ProcessLocator(),
            new Win32WindowLocator(),
            new DefaultDesktopTargetMatcher(),
            new FakeClock(DateTimeOffset.UtcNow));

        var alphaAttachment = await resolver.ResolveAsync(CreateSnapshot(options, alphaId), CancellationToken.None);
        var betaAttachment = await resolver.ResolveAsync(CreateSnapshot(options, betaId), CancellationToken.None);

        Assert.Equal(alpha.ProcessId, alphaAttachment.Process.ProcessId);
        Assert.Equal(beta.ProcessId, betaAttachment.Process.ProcessId);
        Assert.NotNull(alphaAttachment.BaseAddress);
        Assert.NotNull(betaAttachment.BaseAddress);
        Assert.Equal(basePort, alphaAttachment.BaseAddress!.Port);
        Assert.Equal(basePort + 1, betaAttachment.BaseAddress!.Port);
        Assert.Contains(alphaId, alphaAttachment.Window.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(betaId, betaAttachment.Window.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_SupportsMultipleSimultaneousInstancesWithoutCrossBinding()
    {
        const int basePort = 7520;
        var sessionIds = new[] { "attach-many-alpha", "attach-many-beta", "attach-many-gamma" };

        await using var alpha = await TestDesktopAppProcessHost.StartAsync(sessionIds[0], basePort);
        await using var beta = await TestDesktopAppProcessHost.StartAsync(sessionIds[1], basePort + 1);
        await using var gamma = await TestDesktopAppProcessHost.StartAsync(sessionIds[2], basePort + 2);

        var options = CreateDesktopOptions(basePort, sessionIds);
        var resolver = new DefaultSessionAttachmentResolver(
            new ConfiguredDesktopTargetProfileResolver(options),
            new Win32ProcessLocator(),
            new Win32WindowLocator(),
            new DefaultDesktopTargetMatcher(),
            new FakeClock(DateTimeOffset.UtcNow));

        var attachments = new[]
        {
            await resolver.ResolveAsync(CreateSnapshot(options, sessionIds[0]), CancellationToken.None),
            await resolver.ResolveAsync(CreateSnapshot(options, sessionIds[1]), CancellationToken.None),
            await resolver.ResolveAsync(CreateSnapshot(options, sessionIds[2]), CancellationToken.None)
        };

        Assert.Equal(3, attachments.Select(static attachment => attachment.Process.ProcessId).Distinct().Count());
        Assert.Equal(3, attachments.Select(static attachment => attachment.Window.WindowHandle).Distinct().Count());
        Assert.Equal(sessionIds, attachments.Select(static attachment => attachment.SessionId.Value).ToArray());
        Assert.Equal(new[] { basePort, basePort + 1, basePort + 2 }, attachments.Select(static attachment => attachment.BaseAddress!.Port).ToArray());
    }

    private static SessionHostOptions CreateDesktopOptions(int basePort, params string[] sessionIds) =>
        TestOptionsFactory.CreateDesktopTestAppOptions(basePort, sessionIds.Select(id => TestOptionsFactory.Session(id, startupDelayMs: 0)).ToArray());

    private static SessionSnapshot CreateSnapshot(SessionHostOptions options, string sessionId)
    {
        var definition = options.ToSessionDefinitions().Single(definition => definition.Id.Value == sessionId);
        var state = SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with
        {
            DesiredStatus = SessionStatus.Running
        };

        return new SessionSnapshot(definition, state, PendingWorkItems: 0);
    }
}
