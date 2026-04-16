using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
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
        var context = CreateTargetContext(new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName });

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
        var context = CreateTargetContext(new Dictionary<string, string?> { ["BehaviorPack"] = "MissingPack" });

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
            new FixedTargetProfileResolver(CreateTargetContext(new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName })),
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

    private static readonly SessionId SessionId = new("eve-travel");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-15T15:00:00Z");

    private static EveLikeTravelAutopilotBehaviorPack CreatePack() =>
        new(
            new SessionHostOptions { DecisionExecution = new DecisionExecutionOptions { RepeatSuppressionWindowMs = 1_000 } },
            new TravelAutopilotActionSelector(new DefaultUiTreeQueryService()));

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
        SessionOperationalMemorySnapshot? memorySnapshot = null,
        DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? Now;
        var definition = new SessionDefinition(SessionId, "Eve Travel", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), ["test"]);
        var runtime = SessionRuntimeState.Create(definition, effectiveNow) with { CurrentStatus = SessionStatus.Running, DesiredStatus = SessionStatus.Running };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);
        var tree = CreateTree();
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
            CreateTargetContext(new Dictionary<string, string?> { ["BehaviorPack"] = EveLikeTravelAutopilotBehaviorPack.BehaviorPackName, ["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName }),
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

    private static ResolvedDesktopTargetContext CreateTargetContext(IReadOnlyDictionary<string, string?>? metadata = null)
    {
        var profile = new DesktopTargetProfile("eve", DesktopTargetKind.WindowsUiAutomationDesktop, "EveExe", "EVE", null, null, DesktopSessionMatchingMode.WindowTitle, metadata ?? new Dictionary<string, string?>(), true, false);
        var binding = new SessionTargetBinding(SessionId, profile.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var target = new DesktopSessionTarget(SessionId, profile.ProfileName, profile.Kind, profile.MatchingMode, profile.ProcessName, profile.WindowTitleFragment, null, null, profile.Metadata);
        return new ResolvedDesktopTargetContext(SessionId, profile, binding, target, new Dictionary<string, string>());
    }

    private static UiTree CreateTree() =>
        new(
            new UiSnapshotMetadata(SessionId.Value, "test", Now, 1, 1, "EVE", new Dictionary<string, string?>()),
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
