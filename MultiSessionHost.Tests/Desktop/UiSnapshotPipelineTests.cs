using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class UiSnapshotPipelineTests
{
    [Fact]
    public async Task SnapshotPipeline_CapturesNormalizesAndPlansFromTheTestDesktopApp()
    {
        const string sessionId = "snapshot-pipeline";
        const int basePort = 7610;

        await using var host = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(basePort, TestOptionsFactory.Session(sessionId, startupDelayMs: 0));

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        services.AddDesktopSessionServices();
        using var provider = services.BuildServiceProvider();

        var snapshot = CreateSnapshot(options, sessionId);
        var resolver = provider.GetRequiredService<ISessionAttachmentResolver>();
        var profileResolver = provider.GetRequiredService<IDesktopTargetProfileResolver>();
        var snapshotProvider = provider.GetRequiredService<IUiSnapshotProvider>();
        var normalizerResolver = provider.GetRequiredService<IUiTreeNormalizerResolver>();
        var plannerResolver = provider.GetRequiredService<IWorkItemPlannerResolver>();
        var selector = provider.GetRequiredService<IUiNodeSelector>();
        var attachment = await resolver.ResolveAsync(snapshot, CancellationToken.None);
        var context = profileResolver.Resolve(snapshot);
        var normalizer = normalizerResolver.Resolve(context);
        var planner = plannerResolver.Resolve(context);
        var envelope = await snapshotProvider.CaptureAsync(attachment, CancellationToken.None);

        var metadata = new UiSnapshotMetadata(
            sessionId,
            context.Profile.ProfileName,
            envelope.CapturedAtUtc,
            envelope.Process.ProcessId,
            envelope.Window.WindowHandle,
            envelope.Window.Title,
            envelope.Metadata);
        var tree = normalizer.Normalize(metadata, envelope.Root);
        var plannedWorkItems = planner.Plan(tree);

        Assert.Equal(sessionId, envelope.SessionId);
        Assert.Equal(sessionId, tree.Metadata.SessionId);
        Assert.NotNull(selector.FindFirstByRole(tree, "TextBox"));
        Assert.NotNull(selector.FindFirstByExactText(tree, "Start"));
        Assert.NotNull(tree.FindByExactText($"Notes for {sessionId}"));
        Assert.True(tree.Flatten().Count >= 10);
        Assert.Contains(plannedWorkItems, planned => planned.Description.Contains("Start", StringComparison.Ordinal));
    }

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
