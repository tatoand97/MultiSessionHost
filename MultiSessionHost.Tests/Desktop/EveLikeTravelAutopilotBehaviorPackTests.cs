using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.Infrastructure.Queues;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class EveLikeTravelAutopilotBehaviorPackTests
{
    [Fact]
    public void Resolver_SelectsBehaviorPackFromTargetMetadata()
    {
        var pack = CreatePack();
        var resolver = new DefaultTargetBehaviorPackResolver([pack]);
        var context = CreateTargetContext(metadata: new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName });

        var selection = resolver.ResolveSelection(context);

        Assert.NotNull(selection);
        Assert.Equal(EveLikeTravelAutopilotBehaviorPack.BehaviorPackName, selection!.PackName);
        Assert.Same(pack, resolver.ResolvePack(selection.PackName));
    }

    [Fact]
    public void Resolver_ReturnsNoSelectionWithoutMetadata()
    {
        var resolver = new DefaultTargetBehaviorPackResolver([CreatePack()]);

        Assert.Null(resolver.ResolveSelection(CreateTargetContext()));
    }

    [Fact]
    public void Resolver_UnknownPackIsSafeToInspect()
    {
        var resolver = new DefaultTargetBehaviorPackResolver([]);
        var context = CreateTargetContext(metadata: new Dictionary<string, string?> { ["BehaviorPack"] = "MissingPack" });

        var selection = resolver.ResolveSelection(context);

        Assert.Equal("MissingPack", selection!.PackName);
        Assert.Null(resolver.ResolvePack(selection.PackName));
    }

    [Fact]
    public async Task NoRoute_ProducesNoTravelPlanWithExplicitReason()
    {
        var result = await CreatePack().PlanAsync(CreatePlanningContext(routeActive: false, destination: null, currentLocation: "Amarr", nextWaypoint: null), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.NoRoute, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.no-route");
    }

    [Fact]
    public async Task Arrived_ProducesNoProgressCommandWithExplicitReason()
    {
        var result = await CreatePack().PlanAsync(CreatePlanningContext(routeActive: false, destination: "Jita", currentLocation: "Jita", nextWaypoint: null, progressPercent: 100), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.Arrived, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.arrived");
    }

    [Fact]
    public async Task RouteReady_WithWaypointControl_CreatesBoundedNavigatePlan()
    {
        var result = await CreatePack().PlanAsync(CreatePlanningContext(), CancellationToken.None);

        var directive = Assert.Single(result.DecisionPlan.Directives);
        Assert.Equal(DecisionDirectiveKind.Navigate, directive.DirectiveKind);
        Assert.Equal("SelectItem", directive.Metadata["uiCommandKind"]);
        Assert.Equal("Perimeter", directive.Metadata["uiSelectedValue"]);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.plan-next-waypoint");
    }

    [Fact]
    public async Task Transitioning_SuppressesDuplicateProgressAction()
    {
        var context = CreatePlanningContext(
            domainState: CreateDomainState(isTransitioning: true),
            activitySnapshot: SessionActivitySnapshot.CreateBootstrap(SessionId, Now) with { CurrentState = SessionActivityStateKind.Traveling });

        var result = await CreatePack().PlanAsync(context, CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.AwaitingTravelTransition, result.State.StateKind);
        Assert.DoesNotContain(result.DecisionPlan.Directives, directive => directive.DirectiveKind == DecisionDirectiveKind.Navigate);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.awaiting-transition");
    }

    [Fact]
    public async Task PolicyBlocked_ProducesNoPlan()
    {
        var result = await CreatePack().PlanAsync(CreatePlanningContext(policyPaused: true), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.BlockedByPolicy, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.blocked-policy");
    }

    [Fact]
    public async Task OpaqueRootObservability_BlocksTravelPlanningWithExplicitReason()
    {
        var result = await CreatePack().PlanAsync(CreatePlanningContext(opaqueRoot: true), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.ObservabilityInsufficient, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.observability.insufficient");
    }

    [Fact]
    public async Task RecoveryBlocked_ProducesNoTravelAction()
    {
        var recovery = SessionRecoverySnapshot.Create(SessionId) with { IsAttachmentInvalid = true };

        var result = await CreatePack().PlanAsync(CreatePlanningContext(recoverySnapshot: recovery), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.BlockedByRecovery, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.blocked-recovery");
    }

    [Fact]
    public async Task UnsafeRisk_BlocksTravelProgression()
    {
        var risk = new RiskAssessmentResult(
            SessionId,
            Now,
            [],
            new RiskAssessmentSummary(0, 0, 1, RiskSeverity.High, 100, HasWithdrawPolicy: true, "hostile", "Hostile", "Ship", RiskPolicySuggestion.Withdraw),
            []);

        var result = await CreatePack().PlanAsync(CreatePlanningContext(riskAssessment: risk), CancellationToken.None);

        Assert.Equal(TargetBehaviorPlanningStateKind.BlockedByRisk, result.State.StateKind);
        Assert.Empty(result.DecisionPlan.Directives);
        Assert.Contains(result.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.blocked-risk");
    }

    [Fact]
    public async Task MemorySuppression_AvoidsRepeatingSameActionUntilProgressChanges()
    {
        var first = await CreatePack().PlanAsync(CreatePlanningContext(), CancellationToken.None);
        var remembered = SessionOperationalMemorySnapshot.Empty(SessionId, Now) with { Metadata = first.MemoryState.ToMetadata() };

        var repeated = await CreatePack().PlanAsync(CreatePlanningContext(now: Now.AddMilliseconds(500), memorySnapshot: remembered), CancellationToken.None);
        var progressed = await CreatePack().PlanAsync(CreatePlanningContext(now: Now.AddSeconds(2), progressPercent: 65, memorySnapshot: remembered), CancellationToken.None);

        Assert.DoesNotContain(repeated.DecisionPlan.Directives, directive => directive.DirectiveKind == DecisionDirectiveKind.Navigate);
        Assert.Contains(repeated.DecisionPlan.Reasons, reason => reason.Code == "behavior.travel.awaiting-transition");
        Assert.Contains(progressed.DecisionPlan.Directives, directive => directive.DirectiveKind == DecisionDirectiveKind.Navigate);
    }

    [Fact]
    public async Task Planner_PersistsBehaviorDecisionPlanAndRecordsObservability()
    {
        var options = new SessionHostOptions { DecisionExecution = new DecisionExecutionOptions { RepeatSuppressionWindowMs = 1_000 } };
        var definition = new SessionDefinition(SessionId, "Eve Travel", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), ["test"]);
        var runtime = SessionRuntimeState.Create(definition, Now) with { CurrentStatus = SessionStatus.Running, DesiredStatus = SessionStatus.Running };
        var registry = new InMemorySessionRegistry();
        var runtimeStore = new InMemorySessionStateStore();
        var queue = new ChannelBasedWorkQueue();
        var uiStore = new InMemorySessionUiStateStore();
        var domainStore = new InMemorySessionDomainStateStore();
        var semanticStore = new InMemorySessionSemanticExtractionStore();
        var riskStore = new InMemorySessionRiskAssessmentStore();
        var activityStore = new InMemorySessionActivityStateStore();
        var clock = new FakeClock(Now);
        var recoveryStore = new InMemorySessionRecoveryStateStore(options, clock);
        var policyControlStore = new InMemorySessionPolicyControlStore(options, clock);
        var policyControl = new DefaultSessionPolicyControlService(policyControlStore);
        var memoryStore = new InMemorySessionOperationalMemoryStore(options);
        var decisionStore = new InMemorySessionDecisionPlanStore(options);
        var recorder = new CapturingObservabilityRecorder();

        await registry.RegisterAsync(definition, CancellationToken.None);
        await runtimeStore.InitializeAsync(runtime, CancellationToken.None);
        await queue.ResetSessionAsync(SessionId, CancellationToken.None);
        await uiStore.InitializeAsync(SessionUiState.Create(SessionId) with { ProjectedTree = CreateTree(), RawSnapshotJson = "{}", LastSnapshotCapturedAtUtc = Now }, CancellationToken.None);
        await domainStore.InitializeAsync(CreateDomainState(), CancellationToken.None);
        await semanticStore.InitializeAsync(SessionId, CreatePlanningContext().SemanticExtraction!, CancellationToken.None);
        await riskStore.InitializeAsync(SessionId, RiskAssessmentResult.Empty(SessionId, Now), CancellationToken.None);
        await activityStore.UpsertAsync(SessionId, SessionActivitySnapshot.CreateBootstrap(SessionId, Now), CancellationToken.None);
        await memoryStore.InitializeIfMissingAsync(SessionId, Now, CancellationToken.None);
        await decisionStore.InitializeAsync(SessionId, DecisionPlan.Empty(SessionId, Now), CancellationToken.None);

        var planner = new DefaultTargetBehaviorPackPlanner(
            options,
            registry,
            runtimeStore,
            queue,
            new FixedTargetProfileResolver(CreateTargetContext(metadata: new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName })),
            uiStore,
            domainStore,
            semanticStore,
            riskStore,
            activityStore,
            recoveryStore,
            policyControl,
            memoryStore,
            memoryStore,
            decisionStore,
            new DefaultTargetBehaviorPackResolver([CreatePack()]),
            recorder,
            clock,
            NullLogger<DefaultTargetBehaviorPackPlanner>.Instance);

        var result = await planner.TryPlanAsync(SessionId, CancellationToken.None);
        var storedPlan = await decisionStore.GetLatestAsync(SessionId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(storedPlan!.Directives, directive => directive.DirectiveKind == DecisionDirectiveKind.Navigate);
        Assert.Contains(recorder.Activities, activity => activity.Stage == "behavior.pack" && activity.Outcome == "succeeded");
        Assert.Contains(recorder.DecisionReasons, reason => reason.ReasonCode == "behavior.travel.plan-next-waypoint");
    }

    [Fact]
    public async Task ScreenCapture_SelectWaypoint_UsesOcrEvidenceAndEmitsScreenMetadata()
    {
        var selector = CreateScreenSelector();
        var context = CreateScreenPlanningContext();

        var selection = await selector.SelectActionAsync(context, context.SemanticExtraction!.Packages[0].EveLike!, TravelAutopilotMemoryState.Empty, TravelAutopilotActionIntent.SelectWaypoint, CancellationToken.None);

        Assert.NotNull(selection);
        Assert.Equal(TravelAutopilotActionIntent.SelectWaypoint, selection!.Intent);
        Assert.Equal("ocr-line-click", selection.ScreenTarget!.ActionKind);
        Assert.Equal("Perimeter", selection.Command.SelectedValue);
        Assert.Equal("ocr", selection.Command.Metadata["screenEvidenceSource"]);
        Assert.Equal("waypoint", selection.Command.Metadata["screenDiagnostic.matchType"]);
        Assert.Contains("screenRelativeBounds", selection.Command.Metadata.Keys);
    }

    [Fact]
    public async Task ScreenCapture_ToggleAutopilot_UsesTemplateEvidence()
    {
        var selector = CreateScreenSelector(includeAutopilotTemplate: true);
        var context = CreateScreenPlanningContext();

        var selection = await selector.SelectActionAsync(context, context.SemanticExtraction!.Packages[0].EveLike!, TravelAutopilotMemoryState.Empty, TravelAutopilotActionIntent.ToggleAutopilot, CancellationToken.None);

        Assert.NotNull(selection);
        Assert.Equal(TravelAutopilotActionIntent.ToggleAutopilot, selection!.Intent);
        Assert.Equal("template-click", selection.ScreenTarget!.ActionKind);
        Assert.Equal("template", selection.Command.Metadata["screenEvidenceSource"]);
        Assert.Equal("ToggleNode", selection.Command.Metadata["uiCommandKind"]);
    }

    [Fact]
    public async Task ScreenCapture_WeakEvidence_DoesNotProduceAction()
    {
        var selector = CreateScreenSelector(includeWaypointLine: false, includeAutopilotTemplate: false);
        var context = CreateScreenPlanningContext();

        var selection = await selector.SelectActionAsync(context, context.SemanticExtraction!.Packages[0].EveLike!, TravelAutopilotMemoryState.Empty, TravelAutopilotActionIntent.SelectWaypoint, CancellationToken.None);

        Assert.Null(selection);
    }

    private static readonly SessionId SessionId = new("eve-travel");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-15T15:00:00Z");

    private static EveLikeTravelAutopilotBehaviorPack CreatePack() =>
        new(
            new SessionHostOptions { DecisionExecution = new DecisionExecutionOptions { RepeatSuppressionWindowMs = 1_000 } },
            new TravelAutopilotActionSelector(new DefaultUiTreeQueryService()));

    private static ScreenTravelActionSelector CreateScreenSelector(bool includeWaypointLine = true, bool includeAutopilotTemplate = true)
    {
        var sessionId = SessionId;
        var screenSnapshotStore = new InMemorySessionScreenSnapshotStore(new SessionHostOptions { ScreenSnapshots = new ScreenSnapshotStoreOptions { MaxHistoryEntriesPerSession = 5 } });
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var templateStore = new InMemorySessionTemplateDetectionStore();

        var screenSnapshot = CreateScreenSnapshot(sessionId);
        screenSnapshotStore.UpsertLatestAsync(sessionId, screenSnapshot, CancellationToken.None).GetAwaiter().GetResult();

        var preprocessing = CreatePreprocessingResult(sessionId, screenSnapshot.Sequence);
        preprocessingStore.UpsertLatestAsync(sessionId, preprocessing, CancellationToken.None).GetAwaiter().GetResult();

        var ocr = CreateOcrResult(sessionId, screenSnapshot.Sequence, includeWaypointLine);
        ocrStore.UpsertLatestAsync(sessionId, ocr, CancellationToken.None).GetAwaiter().GetResult();

        var templates = CreateTemplateResult(sessionId, screenSnapshot.Sequence, includeAutopilotTemplate);
        templateStore.UpsertLatestAsync(sessionId, templates, CancellationToken.None).GetAwaiter().GetResult();

        return new ScreenTravelActionSelector(new InMemorySessionScreenRegionStoreWithLatest(CreateRegionResolution(sessionId, screenSnapshot.Sequence)), preprocessingStore, ocrStore, templateStore);
    }

    private static TargetBehaviorPlanningContext CreateScreenPlanningContext()
    {
        var now = Now;
        var definition = new SessionDefinition(SessionId, "Eve Travel", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), ["test"]);
        var runtime = SessionRuntimeState.Create(definition, now) with { CurrentStatus = SessionStatus.Running, DesiredStatus = SessionStatus.Running };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);
        var semantic = new UiSemanticExtractionResult(
            SessionId,
            now,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [new TargetSemanticPackageResult(EveLikeSemanticPackage.SemanticPackageName, EveLikeSemanticPackage.SemanticPackageVersion, true, DetectionConfidence.High, [], new Dictionary<string, DetectionConfidence>(), null, CreatePackage(true, "Jita", "Amarr", "Perimeter", 40))],
            [],
            new Dictionary<string, DetectionConfidence>());

        return new TargetBehaviorPlanningContext(
            snapshot,
            SessionUiState.Create(SessionId),
            CreateDomainState(),
            null,
            semantic,
            RiskAssessmentResult.Empty(SessionId, now),
            SessionRecoverySnapshot.Create(SessionId),
            SessionActivitySnapshot.CreateBootstrap(SessionId, now),
            null,
            SessionPolicyControlState.Create(SessionId),
            CreateTargetContext(DesktopTargetKind.ScreenCaptureDesktop, new Dictionary<string, string?>
            {
                ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName,
                ["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName,
                ["EveLike.ScreenTravelMinActionConfidence"] = "0.50"
            }),
            now);
    }

    private static SessionScreenSnapshot CreateScreenSnapshot(SessionId sessionId) =>
        new(
            sessionId,
            Sequence: 7,
            CapturedAtUtc: Now,
            ProcessId: 1234,
            ProcessName: "Eve",
            WindowHandle: 999,
            WindowTitle: "EVE",
            new UiBounds(100, 200, 800, 600),
            ImageWidth: 800,
            ImageHeight: 600,
            ImageFormat: "image/png",
            PixelFormat: "Format32bppArgb",
            ImageBytes: [1, 2, 3],
            PayloadByteLength: 3,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "ScreenCapture",
            "FakeCapture",
            1.0d,
            "LiveRefresh",
            new Dictionary<string, string?>(StringComparer.Ordinal));

    private static SessionFramePreprocessingResult CreatePreprocessingResult(SessionId sessionId, long sequence) =>
        new(
            sessionId,
            Now,
            sequence,
            Now,
            sequence,
            Now,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultFramePreprocessing",
            1,
            1,
            0,
            [new ProcessedFrameArtifact("region:window.right.threshold", "threshold", sequence, "window.right", 800, 600, "image/png", 3, ["crop"], [], [], new Dictionary<string, string?>(StringComparer.Ordinal), [1, 2, 3])],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));

    private static SessionOcrExtractionResult CreateOcrResult(SessionId sessionId, long sequence, bool includeWaypointLine)
    {
        IReadOnlyList<OcrTextLine> lines = includeWaypointLine
            ? [new OcrTextLine("Perimeter", "Perimeter", 0.97d, new UiBounds(40, 120, 160, 22), "region:window.right.threshold", "window.right")]
            : Array.Empty<OcrTextLine>();

        return new SessionOcrExtractionResult(
            sessionId,
            Now,
            sequence,
            Now,
            sequence,
            Now,
            Now,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultRegionOcr",
            "FakeOcr",
            "FakeBackend",
            1,
            lines.Count,
            0,
            [new OcrArtifactResult("region:window.right.threshold", "window.right", "threshold", ["crop"], string.Join(' ', lines.Select(line => line.Text)), string.Join(' ', lines.Select(line => line.NormalizedText)), 0.97d, lines.Count, lines.Count, "DefaultRegionAwareOcrSelectionV1", false, [], lines, [], [], new Dictionary<string, string?>(StringComparer.Ordinal))],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    private static SessionTemplateDetectionResult CreateTemplateResult(SessionId sessionId, long sequence, bool includeAutopilotTemplate) =>
        new(
            sessionId,
            Now,
            sequence,
            Now,
            sequence,
            Now,
            Now,
            Now,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultTemplateDetection",
            "DefaultTemplateSet",
            "DeterministicTemplateMatcher",
            "FakeBackend",
            1,
            1,
            1,
            0,
            [new TemplateArtifactResult("region:window.right.threshold", "window.right", "threshold", "match", false, 1, includeAutopilotTemplate ? 1 : 0, includeAutopilotTemplate ? [new TemplateMatch("autopilot-toggle", "toggle", 0.96d, new UiBounds(200, 40, 100, 30), "region:window.right.threshold", "window.right", 0.96d, 0.90d, new Dictionary<string, string?>(StringComparer.Ordinal))] : [], [], [], new Dictionary<string, string?>(StringComparer.Ordinal))],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));

    private static SessionScreenRegionResolution CreateRegionResolution(SessionId sessionId, long sequence) =>
        new(
            sessionId,
            Now,
            sequence,
            Now,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "screen-profile",
            "DefaultDesktopGrid",
            "DefaultDesktopGridRegionLocator",
            "DefaultDesktopGridRegionLocator",
            800,
            600,
            1,
            1,
            0,
            [new ScreenRegionMatch("window.right", "panel", new UiBounds(0, 0, 200, 600), 0.95d, "DefaultDesktopGridRegionLocator", "Derived from layout", ScreenRegionMatchState.Matched, null, 800, 600, new Dictionary<string, string?>(StringComparer.Ordinal))],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));

    private sealed class InMemorySessionScreenRegionStoreWithLatest : ISessionScreenRegionStore
    {
        private readonly SessionScreenRegionResolution _resolution;

        public InMemorySessionScreenRegionStoreWithLatest(SessionScreenRegionResolution resolution)
        {
            _resolution = resolution;
        }

        public ValueTask<SessionScreenRegionResolution> UpsertLatestAsync(SessionId sessionId, SessionScreenRegionResolution resolution, CancellationToken cancellationToken) => ValueTask.FromResult(resolution);

        public ValueTask<SessionScreenRegionResolution?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken) => ValueTask.FromResult<SessionScreenRegionResolution?>(_resolution);

        public ValueTask<IReadOnlyCollection<SessionScreenRegionResolution>> GetAllLatestAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyCollection<SessionScreenRegionResolution>>([_resolution]);

        public ValueTask<SessionScreenRegionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken) => ValueTask.FromResult<SessionScreenRegionSummary?>(_resolution.ToSummary());

        public ValueTask<IReadOnlyCollection<SessionScreenRegionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyCollection<SessionScreenRegionSummary>>([_resolution.ToSummary()]);
    }

    private static TargetBehaviorPlanningContext CreatePlanningContext(
        bool routeActive = true,
        string? destination = "Jita",
        string? currentLocation = "Amarr",
        string? nextWaypoint = "Perimeter",
        double? progressPercent = 40,
        SessionDomainState? domainState = null,
        SessionActivitySnapshot? activitySnapshot = null,
        SessionRecoverySnapshot? recoverySnapshot = null,
        RiskAssessmentResult? riskAssessment = null,
        bool policyPaused = false,
        bool opaqueRoot = false,
        SessionOperationalMemorySnapshot? memorySnapshot = null,
        DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? Now;
        var definition = new SessionDefinition(SessionId, "Eve Travel", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), ["test"]);
        var runtime = SessionRuntimeState.Create(definition, effectiveNow) with { CurrentStatus = SessionStatus.Running, DesiredStatus = SessionStatus.Running };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);
        var tree = CreateTree(opaqueRoot);
        var package = CreatePackage(routeActive, destination, currentLocation, nextWaypoint, progressPercent);
        var semantic = new UiSemanticExtractionResult(
            SessionId,
            effectiveNow,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [new TargetSemanticPackageResult(EveLikeSemanticPackage.SemanticPackageName, EveLikeSemanticPackage.SemanticPackageVersion, true, DetectionConfidence.High, [], new Dictionary<string, DetectionConfidence>(), null, package)],
            [],
            new Dictionary<string, DetectionConfidence>());

        return new TargetBehaviorPlanningContext(
            snapshot,
            SessionUiState.Create(SessionId) with { ProjectedTree = tree, RawSnapshotJson = "{}", LastSnapshotCapturedAtUtc = effectiveNow },
            domainState ?? CreateDomainState(),
            CurrentDecisionPlan: null,
            semantic,
            riskAssessment ?? RiskAssessmentResult.Empty(SessionId, effectiveNow),
            recoverySnapshot ?? SessionRecoverySnapshot.Create(SessionId),
            activitySnapshot ?? SessionActivitySnapshot.CreateBootstrap(SessionId, effectiveNow),
            memorySnapshot,
            SessionPolicyControlState.Create(SessionId) with { IsPolicyPaused = policyPaused },
            CreateTargetContext(metadata: new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName, ["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName }),
            effectiveNow);
    }

    private static SessionDomainState CreateDomainState(bool isTransitioning = false) =>
        SessionDomainState.CreateBootstrap(SessionId, Now) with
        {
            Navigation = new NavigationState(NavigationStatus.Idle, isTransitioning, "Jita", "Amarr -> Jita", 40, Now.AddMinutes(-1), Now)
        };

    private static EveLikeSemanticPackageResult CreatePackage(bool routeActive, string? destination, string? currentLocation, string? nextWaypoint, double? progressPercent) =>
        new(
            EveLikeSemanticPackage.SemanticPackageName,
            EveLikeSemanticPackage.SemanticPackageVersion,
            new LocalPresenceSnapshot(true, "Local", 0, 0, [], DetectionConfidence.High, []),
            new TravelRouteSnapshot(routeActive, destination, currentLocation, nextWaypoint, nextWaypoint is null ? 0 : 3, nextWaypoint is null ? [] : ["Niarja", "Tama", nextWaypoint], progressPercent, DetectionConfidence.High, []),
            [],
            [],
            new TacticalSnapshot([], 0, [], [], [], DetectionConfidence.High, []),
            new SafetyLocationSemantic(true, "Station", HideAvailable: false, DockedHint: true, TetheredHint: false, EscapeRouteLabel: null, DetectionConfidence.High, []),
            [],
            new Dictionary<string, DetectionConfidence>());

    private static ResolvedDesktopTargetContext CreateTargetContext(DesktopTargetKind kind = DesktopTargetKind.WindowsUiAutomationDesktop, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        var profile = new DesktopTargetProfile("eve", kind, "EveExe", "EVE", null, null, DesktopSessionMatchingMode.WindowTitle, metadata ?? new Dictionary<string, string?>(), true, false);
        var binding = new SessionTargetBinding(SessionId, profile.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var target = new DesktopSessionTarget(SessionId, profile.ProfileName, profile.Kind, profile.MatchingMode, profile.ProcessName, profile.WindowTitleFragment, null, null, profile.Metadata);
        return new ResolvedDesktopTargetContext(SessionId, profile, binding, target, new Dictionary<string, string>());
    }

    private static UiTree CreateTree(bool opaqueRoot = false) =>
        new(
            new UiSnapshotMetadata(SessionId.Value, "test", Now, 1, 1, "EVE", new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["opaqueRoot"] = opaqueRoot.ToString(),
                ["observabilityMode"] = opaqueRoot ? "RootOnly" : "ObservableTree"
            }),
            Node("root", "Window", text: "EVE", children:
            [
                Node("routePanel", "ListBox", name: "Route", attributes: [new UiAttribute("semanticRole", "route")], children:
                [
                    Node("route-1", "ListItem", text: "Niarja"),
                    Node("route-2", "ListItem", text: "Tama"),
                    Node("route-3", "ListItem", text: "Perimeter")
                ]),
                Node("autopilot", "ToggleButton", name: "Autopilot", attributes: [new UiAttribute("semanticActions", "toggle")]),
                Node("jump", "Button", name: "Jump", attributes: [new UiAttribute("semanticActions", "invoke")])
            ]));

    private static UiNode Node(string id, string role, string? name = null, string? text = null, IReadOnlyList<UiAttribute>? attributes = null, IReadOnlyList<UiNode>? children = null) =>
        new(new UiNodeId(id), role, name, text, Bounds: null, Visible: true, Enabled: true, Selected: false, attributes ?? [], children ?? []);

    private sealed class FixedTargetProfileResolver : MultiSessionHost.Desktop.Interfaces.IDesktopTargetProfileResolver
    {
        private readonly ResolvedDesktopTargetContext _context;

        public FixedTargetProfileResolver(ResolvedDesktopTargetContext context)
        {
            _context = context;
        }

        public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => [_context.Profile];

        public DesktopTargetProfile? TryGetProfile(string profileName) => _context.Profile.ProfileName == profileName ? _context.Profile : null;

        public SessionTargetBinding? TryGetBinding(SessionId sessionId) => _context.Binding.SessionId == sessionId ? _context.Binding : null;

        public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot) => _context;
    }

    private sealed class CapturingObservabilityRecorder : NoOpObservabilityRecorder
    {
        public List<(string Stage, string Outcome)> Activities { get; } = [];

        public List<(string Category, string ReasonCode)> DecisionReasons { get; } = [];

        public override ValueTask RecordActivityAsync(SessionId sessionId, string stage, string outcome, TimeSpan duration, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
        {
            Activities.Add((stage, outcome));
            return ValueTask.CompletedTask;
        }

        public override ValueTask RecordDecisionReasonAsync(SessionId sessionId, string category, string reasonCode, string? reason, string? sourceComponent, CancellationToken cancellationToken)
        {
            DecisionReasons.Add((category, reasonCode));
            return ValueTask.CompletedTask;
        }
    }
}
