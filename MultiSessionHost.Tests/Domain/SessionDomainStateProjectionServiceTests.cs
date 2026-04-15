using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;

namespace MultiSessionHost.Tests.Domain;

public sealed class SessionDomainStateProjectionServiceTests
{
    [Fact]
    public void Project_ProducesCoherentSnapshotWhenUiStateExists()
    {
        var now = DateTimeOffset.Parse("2026-04-15T12:30:00Z");
        var sessionId = new SessionId("domain-project-ui");
        var current = SessionDomainState.CreateBootstrap(sessionId, now.AddMinutes(-1));
        var snapshot = CreateSnapshot(sessionId, pendingWorkItems: 0);
        var context = CreateContext(sessionId);
        var uiState = SessionUiState.Create(sessionId) with
        {
            RawSnapshotJson = "{}",
            LastSnapshotCapturedAtUtc = now.AddSeconds(-5),
            LastRefreshCompletedAtUtc = now
        };
        var service = new DefaultSessionDomainStateProjectionService();

        var projected = service.Project(current, snapshot, context, uiState, attachment: null, semanticExtraction: null, now);

        Assert.Equal(DomainSnapshotSource.UiProjection, projected.Source);
        Assert.Equal(2, projected.Version);
        Assert.Equal(now.AddSeconds(-5), projected.CapturedAtUtc);
        Assert.Equal(NavigationStatus.Idle, projected.Navigation.Status);
        Assert.Equal(CombatStatus.Idle, projected.Combat.Status);
        Assert.Equal(TargetingStatus.None, projected.Target.Status);
        Assert.Equal("UnitTestSource", projected.Location.ContextLabel);
        Assert.False(projected.Location.IsUnknown);
        Assert.Contains(projected.Warnings, warning => warning.Contains("No desktop attachment", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_ProducesCoherentSnapshotWhenUiStateIsAbsent()
    {
        var now = DateTimeOffset.Parse("2026-04-15T12:45:00Z");
        var sessionId = new SessionId("domain-project-no-ui");
        var current = SessionDomainState.CreateBootstrap(sessionId, now.AddMinutes(-1));
        var snapshot = CreateSnapshot(sessionId, pendingWorkItems: 1);
        var context = CreateContext(sessionId);
        var service = new DefaultSessionDomainStateProjectionService();

        var projected = service.Project(current, snapshot, context, uiState: null, attachment: null, semanticExtraction: null, now);

        Assert.Equal(DomainSnapshotSource.UiProjection, projected.Source);
        Assert.Equal(NavigationStatus.InProgress, projected.Navigation.Status);
        Assert.True(projected.Navigation.IsTransitioning);
        Assert.Null(projected.CapturedAtUtc);
        Assert.Contains(projected.Warnings, warning => warning.Contains("No UI state", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_RecordsWarningsWhenUiStateIsDegraded()
    {
        var now = DateTimeOffset.Parse("2026-04-15T13:00:00Z");
        var sessionId = new SessionId("domain-project-degraded");
        var current = SessionDomainState.CreateBootstrap(sessionId, now.AddMinutes(-1));
        var snapshot = CreateSnapshot(sessionId, pendingWorkItems: 0);
        var context = CreateContext(sessionId);
        var uiState = SessionUiState.Create(sessionId) with
        {
            LastRefreshError = "Projection failed."
        };
        var service = new DefaultSessionDomainStateProjectionService();

        var projected = service.Project(current, snapshot, context, uiState, attachment: null, semanticExtraction: null, now);

        Assert.True(projected.Resources.IsDegraded);
        Assert.Contains(projected.Warnings, warning => warning.Contains("No raw UI snapshot", StringComparison.Ordinal));
        Assert.Contains(projected.Warnings, warning => warning.Contains("No projected UI tree", StringComparison.Ordinal));
        Assert.Contains(projected.Warnings, warning => warning.Contains("Projection failed.", StringComparison.Ordinal));
    }

    private static SessionSnapshot CreateSnapshot(SessionId sessionId, int pendingWorkItems)
    {
        var definition = new SessionDefinition(
            sessionId,
            $"{sessionId.Value}-display",
            Enabled: true,
            TickInterval: TimeSpan.FromSeconds(1),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 3,
            InitialBackoff: TimeSpan.FromMilliseconds(100),
            Tags: []);
        var runtime = SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with
        {
            CurrentStatus = SessionStatus.Running,
            DesiredStatus = SessionStatus.Running
        };

        return new SessionSnapshot(definition, runtime, pendingWorkItems);
    }

    private static ResolvedDesktopTargetContext CreateContext(SessionId sessionId)
    {
        var profile = new DesktopTargetProfile(
            "unit-test",
            DesktopTargetKind.DesktopTestApp,
            "UnitTestProcess",
            WindowTitleFragment: null,
            CommandLineFragmentTemplate: null,
            BaseAddressTemplate: null,
            DesktopSessionMatchingMode.WindowTitle,
            new Dictionary<string, string?> { ["UiSource"] = "UnitTestProfileSource" },
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: true);
        var binding = new SessionTargetBinding(sessionId, profile.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var target = new DesktopSessionTarget(
            sessionId,
            profile.ProfileName,
            profile.Kind,
            profile.MatchingMode,
            profile.ProcessName,
            WindowTitleFragment: null,
            CommandLineFragment: null,
            BaseAddress: null,
            new Dictionary<string, string?> { ["UiSource"] = "UnitTestSource" });

        return new ResolvedDesktopTargetContext(sessionId, profile, binding, target, new Dictionary<string, string>());
    }
}
