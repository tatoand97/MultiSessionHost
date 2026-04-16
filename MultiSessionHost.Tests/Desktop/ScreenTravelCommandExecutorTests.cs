using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class ScreenTravelCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ClicksAbsoluteCenterFromRelativeBounds()
    {
        var fixture = CreateFixture();
        var command = CreateScreenTravelCommand(fixture.SessionId, new UiBounds(40, 120, 160, 22), sourceSequence: 7, TravelAutopilotActionIntent.SelectWaypoint, "ocr", "waypoint");
        var executor = new ScreenTravelCommandExecutor(fixture.ScreenSnapshotStore, fixture.InputDriver, fixture.RecoveryStore, new NoOpObservabilityRecorder(), fixture.Clock, NullLogger<ScreenTravelCommandExecutor>.Instance);

        var result = await executor.ExecuteAsync(fixture.Context, fixture.Attachment, command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(220, fixture.InputDriver.Clicks.Single().X);
        Assert.Equal(331, fixture.InputDriver.Clicks.Single().Y);
    }

    [Fact]
    public async Task ExecuteAsync_FailsExplicitly_WhenBoundsDoNotFitSnapshot()
    {
        var fixture = CreateFixture();
        var command = CreateScreenTravelCommand(fixture.SessionId, new UiBounds(700, 500, 200, 200), sourceSequence: 7, TravelAutopilotActionIntent.InvokeTravelControl, "template", "travel-control");
        var executor = new ScreenTravelCommandExecutor(fixture.ScreenSnapshotStore, fixture.InputDriver, fixture.RecoveryStore, new NoOpObservabilityRecorder(), fixture.Clock, NullLogger<ScreenTravelCommandExecutor>.Instance);

        var result = await executor.ExecuteAsync(fixture.Context, fixture.Attachment, command, CancellationToken.None);
        var recovery = await fixture.RecoveryStore.GetAsync(fixture.SessionId, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(UiCommandFailureCodes.ScreenTravelBoundsInvalid, result.FailureCode);
        Assert.Empty(fixture.InputDriver.Clicks);
        Assert.Equal(1, recovery.FailureCountsByCategory[SessionRecoveryFailureCategory.CommandExecutionFailure]);
    }

    [Fact]
    public async Task UiCommandExecutor_ExecutesScreenTravelCommandAndRefreshesAfterSuccess()
    {
        var fixture = CreateFixture();
        var refreshService = new StubRefreshService();
        var uiCommandExecutor = new UiCommandExecutor(
            new StubSessionCoordinator(fixture.Session),
            new StubDesktopTargetProfileResolver(fixture.Context),
            new StubSessionAttachmentOperations(fixture.Attachment),
            new InMemoryExecutionCoordinator(fixture.Options, fixture.Clock, NullLogger<InMemoryExecutionCoordinator>.Instance),
            new MultiSessionHost.Desktop.Targets.DefaultExecutionResourceResolver(fixture.Options, fixture.Clock),
            refreshService,
            new ThrowingUiActionResolver(),
            fixture.ScreenTravelExecutor,
            [],
            new NoOpObservabilityRecorder(),
            fixture.Clock,
            NullLogger<UiCommandExecutor>.Instance);

        var command = CreateScreenTravelCommand(fixture.SessionId, new UiBounds(40, 120, 160, 22), sourceSequence: 7, TravelAutopilotActionIntent.SelectWaypoint, "ocr", "waypoint");
        var result = await uiCommandExecutor.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.InputDriver.Clicks.Count);
        Assert.Equal(1, refreshService.RefreshCalls);
    }

    private static UiCommand CreateScreenTravelCommand(
        SessionId sessionId,
        UiBounds bounds,
        long sourceSequence,
        TravelAutopilotActionIntent intent,
        string evidenceSource,
        string actionKind)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["screenTravelIntent"] = intent.ToString(),
            ["screenActionKind"] = actionKind,
            ["screenEvidenceSource"] = evidenceSource,
            ["screenRegionName"] = "window.right",
            ["screenArtifactName"] = "region:window.right.threshold",
            ["screenCandidateLabel"] = "Perimeter",
            ["screenRelativeBounds"] = $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}",
            ["screenSourceSnapshotSequence"] = sourceSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["screenSelectionConfidence"] = "0.97",
            ["screenExplanation"] = "deterministic test fixture",
            ["screenDiagnostic.matchType"] = actionKind,
            ["uiCommandKind"] = intent == TravelAutopilotActionIntent.SelectWaypoint ? UiCommandKind.SelectItem.ToString() : intent == TravelAutopilotActionIntent.ToggleAutopilot ? UiCommandKind.ToggleNode.ToString() : UiCommandKind.InvokeNodeAction.ToString(),
            ["uiSelectedValue"] = intent == TravelAutopilotActionIntent.SelectWaypoint ? "Perimeter" : null,
            ["uiActionName"] = intent == TravelAutopilotActionIntent.InvokeTravelControl ? "default" : null,
            ["uiBoolValue"] = intent == TravelAutopilotActionIntent.ToggleAutopilot ? bool.TrueString : null
        };

        return new UiCommand(sessionId, null, intent == TravelAutopilotActionIntent.SelectWaypoint ? UiCommandKind.SelectItem : intent == TravelAutopilotActionIntent.ToggleAutopilot ? UiCommandKind.ToggleNode : UiCommandKind.InvokeNodeAction, metadata["uiActionName"], null, intent == TravelAutopilotActionIntent.ToggleAutopilot ? true : null, metadata["uiSelectedValue"], metadata);
    }

    private static Fixture CreateFixture()
    {
        var sessionId = new SessionId("alpha");
        var options = new SessionHostOptions
        {
            ScreenSnapshots = new ScreenSnapshotStoreOptions { MaxHistoryEntriesPerSession = 5 },
            RuntimePersistence = new RuntimePersistenceOptions { EnableRuntimePersistence = false },
            DecisionExecution = new DecisionExecutionOptions { EnableDecisionExecution = true }
        };
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-16T12:00:00Z"));
        var screenSnapshotStore = new InMemorySessionScreenSnapshotStore(options);
        var recoveryStore = new InMemorySessionRecoveryStateStore(options, clock);
        var snapshot = new SessionScreenSnapshot(
            sessionId,
            7,
            clock.UtcNow,
            1234,
            "Eve",
            999,
            "EVE",
            new UiBounds(100, 200, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            [1, 2, 3],
            3,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "ScreenCapture",
            "FakeCapture",
            1.0d,
            "LiveRefresh",
            new Dictionary<string, string?>(StringComparer.Ordinal));
        screenSnapshotStore.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None).GetAwaiter().GetResult();

        var process = new DesktopProcessInfo(1234, "Eve", null, 999);
        var window = new DesktopWindowInfo(999, 1234, "EVE", true);
        var profile = new DesktopTargetProfile(
            "screen-profile",
            DesktopTargetKind.ScreenCaptureDesktop,
            "Eve",
            "EVE",
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName },
            true,
            false);
        var target = new DesktopSessionTarget(sessionId, profile.ProfileName, profile.Kind, profile.MatchingMode, profile.ProcessName, profile.WindowTitleFragment, null, null, profile.Metadata);
        var context = new ResolvedDesktopTargetContext(sessionId, profile, new SessionTargetBinding(sessionId, profile.ProfileName, new Dictionary<string, string>(), null), target, new Dictionary<string, string>());
        var attachment = new DesktopSessionAttachment(sessionId, target, process, window, null, clock.UtcNow);
        var inputDriver = new RecordingScreenTravelInputDriver();
        var screenTravelExecutor = new ScreenTravelCommandExecutor(screenSnapshotStore, inputDriver, recoveryStore, new NoOpObservabilityRecorder(), clock, NullLogger<ScreenTravelCommandExecutor>.Instance);
        var session = new SessionSnapshot(new SessionDefinition(sessionId, "Eve", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.Zero, ["test"]), SessionRuntimeState.Create(new SessionDefinition(sessionId, "Eve", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.Zero, ["test"]), clock.UtcNow) with { CurrentStatus = SessionStatus.Running, DesiredStatus = SessionStatus.Running }, 0);

        return new Fixture(options, clock, sessionId, screenSnapshotStore, recoveryStore, inputDriver, screenTravelExecutor, context, attachment, session);
    }

    private sealed record Fixture(
        SessionHostOptions Options,
        FakeClock Clock,
        SessionId SessionId,
        ISessionScreenSnapshotStore ScreenSnapshotStore,
        InMemorySessionRecoveryStateStore RecoveryStore,
        RecordingScreenTravelInputDriver InputDriver,
        ScreenTravelCommandExecutor ScreenTravelExecutor,
        ResolvedDesktopTargetContext Context,
        DesktopSessionAttachment Attachment,
        SessionSnapshot Session);

    private sealed class RecordingScreenTravelInputDriver : IScreenTravelInputDriver
    {
        public List<(int X, int Y)> Clicks { get; } = [];

        public Task<bool> ClickAsync(int x, int y, CancellationToken cancellationToken)
        {
            Clicks.Add((x, y));
            return Task.FromResult(true);
        }
    }

    private sealed class StubSessionCoordinator : ISessionCoordinator
    {
        private readonly SessionSnapshot _session;

        public StubSessionCoordinator(SessionSnapshot session)
        {
            _session = session;
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RunSchedulerCycleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public IReadOnlyCollection<SessionSnapshot> GetSessions() => [_session];
        public SessionSnapshot? GetSession(SessionId sessionId) => sessionId == _session.SessionId ? _session : null;
        public SessionUiState? GetSessionUiState(SessionId sessionId) => SessionUiState.Create(sessionId);
        public SessionDomainState? GetSessionDomainState(SessionId sessionId) => SessionDomainState.CreateBootstrap(sessionId, DateTimeOffset.UtcNow);
        public IReadOnlyCollection<SessionDomainState> GetSessionDomainStates() => [];
        public Task<SessionUiState> RefreshSessionUiAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.FromResult(SessionUiState.Create(sessionId));
        public ProcessHealthSnapshot GetProcessHealth() => new(DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0, []);
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubDesktopTargetProfileResolver : IDesktopTargetProfileResolver
    {
        private readonly ResolvedDesktopTargetContext _context;

        public StubDesktopTargetProfileResolver(ResolvedDesktopTargetContext context)
        {
            _context = context;
        }

        public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => [_context.Profile];
        public DesktopTargetProfile? TryGetProfile(string profileName) => _context.Profile;
        public SessionTargetBinding? TryGetBinding(SessionId sessionId) => _context.Binding;
        public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot) => _context;
    }

    private sealed class StubSessionAttachmentOperations : ISessionAttachmentOperations
    {
        private readonly DesktopSessionAttachment _attachment;

        public StubSessionAttachmentOperations(DesktopSessionAttachment attachment)
        {
            _attachment = attachment;
        }

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, CancellationToken cancellationToken) => Task.FromResult(_attachment);
        public Task<bool> InvalidateAsync(SessionId sessionId, DesktopSessionAttachment currentAttachment, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubRefreshService : ISessionUiRefreshService
    {
        public int RefreshCalls { get; private set; }

        public Task<SessionUiState> CaptureAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken) => Task.FromResult(SessionUiState.Create(snapshot.SessionId));
        public Task<SessionUiState> ProjectAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment? attachment, CancellationToken cancellationToken) => Task.FromResult(SessionUiState.Create(snapshot.SessionId));
        public Task<SessionUiState> RefreshAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(SessionUiState.Create(snapshot.SessionId));
        }
    }

    private sealed class ThrowingUiActionResolver : IUiActionResolver
    {
        public ResolvedUiAction Resolve(UiTree tree, UiCommand command) => throw new InvalidOperationException("UIA resolver should not be used for screen travel commands.");
    }
}
