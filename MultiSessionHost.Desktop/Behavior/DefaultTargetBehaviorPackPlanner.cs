using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class DefaultTargetBehaviorPackPlanner : ITargetBehaviorPackPlanner
{
    private readonly SessionHostOptions _options;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IWorkQueue _workQueue;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly ISessionDomainStateStore _sessionDomainStateStore;
    private readonly ISessionSemanticExtractionStore _semanticExtractionStore;
    private readonly ISessionRiskAssessmentStore _riskAssessmentStore;
    private readonly ISessionActivityStateStore _activityStateStore;
    private readonly ISessionRecoveryStateStore _recoveryStateStore;
    private readonly ISessionPolicyControlService _policyControlService;
    private readonly ISessionOperationalMemoryReader _operationalMemoryReader;
    private readonly ISessionOperationalMemoryStore _operationalMemoryStore;
    private readonly ISessionDecisionPlanStore _decisionPlanStore;
    private readonly ITargetBehaviorPackResolver _behaviorPackResolver;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly IClock _clock;
    private readonly ILogger<DefaultTargetBehaviorPackPlanner> _logger;

    public DefaultTargetBehaviorPackPlanner(
        SessionHostOptions options,
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        IWorkQueue workQueue,
        IDesktopTargetProfileResolver targetProfileResolver,
        ISessionUiStateStore sessionUiStateStore,
        ISessionDomainStateStore sessionDomainStateStore,
        ISessionSemanticExtractionStore semanticExtractionStore,
        ISessionRiskAssessmentStore riskAssessmentStore,
        ISessionActivityStateStore activityStateStore,
        ISessionRecoveryStateStore recoveryStateStore,
        ISessionPolicyControlService policyControlService,
        ISessionOperationalMemoryReader operationalMemoryReader,
        ISessionOperationalMemoryStore operationalMemoryStore,
        ISessionDecisionPlanStore decisionPlanStore,
        ITargetBehaviorPackResolver behaviorPackResolver,
        IObservabilityRecorder observabilityRecorder,
        IClock clock,
        ILogger<DefaultTargetBehaviorPackPlanner> logger)
    {
        _options = options;
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _workQueue = workQueue;
        _targetProfileResolver = targetProfileResolver;
        _sessionUiStateStore = sessionUiStateStore;
        _sessionDomainStateStore = sessionDomainStateStore;
        _semanticExtractionStore = semanticExtractionStore;
        _riskAssessmentStore = riskAssessmentStore;
        _activityStateStore = activityStateStore;
        _recoveryStateStore = recoveryStateStore;
        _policyControlService = policyControlService;
        _operationalMemoryReader = operationalMemoryReader;
        _operationalMemoryStore = operationalMemoryStore;
        _decisionPlanStore = decisionPlanStore;
        _behaviorPackResolver = behaviorPackResolver;
        _observabilityRecorder = observabilityRecorder;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<TargetBehaviorPlanningResult?> TryPlanAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var definition = _sessionRegistry.GetById(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var runtime = await _sessionStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Runtime state for session '{sessionId}' was not initialized.");
        var snapshot = new SessionSnapshot(definition, runtime, _workQueue.GetPendingCount(sessionId));
        var targetContext = _targetProfileResolver.Resolve(snapshot);
        var selection = _behaviorPackResolver.ResolveSelection(targetContext);

        if (selection is null)
        {
            return null;
        }

        var uiState = await _sessionUiStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var domainState = await _sessionDomainStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Domain state for session '{sessionId}' was not initialized.");
        var semanticExtraction = await _semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var riskAssessment = await _riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var activityState = await _activityStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var recoveryState = await _recoveryStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var policyGate = await _policyControlService.GetEvaluationGateAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var operationalMemory = await _operationalMemoryReader.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var currentDecisionPlan = await _decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var pack = _behaviorPackResolver.ResolvePack(selection.PackName);

        var planningContext = new TargetBehaviorPlanningContext(
            snapshot,
            uiState,
            domainState,
            currentDecisionPlan,
            semanticExtraction,
            riskAssessment,
            recoveryState,
            activityState,
            operationalMemory,
            policyGate.State,
            targetContext,
            now);

        TargetBehaviorPlanningResult result;
        if (pack is null)
        {
            result = BuildUnknownPackResult(planningContext, selection.PackName, selection.MetadataKey);
            await RecordPlanningAsync(sessionId, result, isFailure: true, cancellationToken).ConfigureAwait(false);
            return result;
        }

        result = await pack.PlanAsync(planningContext, cancellationToken).ConfigureAwait(false);
        await _decisionPlanStore.UpdateAsync(sessionId, result.DecisionPlan, cancellationToken).ConfigureAwait(false);
        await PersistTravelMemoryAsync(sessionId, operationalMemory, result.MemoryState, cancellationToken).ConfigureAwait(false);
        await RecordPlanningAsync(sessionId, result, isFailure: false, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static TargetBehaviorPlanningResult BuildUnknownPackResult(
        TargetBehaviorPlanningContext context,
        string packName,
        string metadataKey)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["behaviorPack"] = packName,
            ["selectionMetadataKey"] = metadataKey,
            ["outcome"] = "unknown-pack"
        };

        var reason = new DecisionReason(
            packName,
            BehaviorReasonCodes.UnknownBehaviorPack,
            $"Behavior pack '{packName}' is not registered.",
            metadata);

        var plan = new DecisionPlan(
            context.SessionSnapshot.SessionId,
            context.Now,
            DecisionPlanStatus.Blocked,
            [],
            [reason],
            new PolicyExecutionSummary([packName], [], [], [], 0, 0, new Dictionary<string, int>(StringComparer.Ordinal)),
            [$"Behavior pack '{packName}' is not registered."],
            new DecisionPlanExplanation([], [], [], [$"Behavior pack '{packName}' is not registered."], [reason.Code]));

        var state = new TargetBehaviorPlanningState(
            packName,
            TargetBehaviorPlanningStateKind.Unknown,
            string.Empty,
            RouteActive: false,
            RouteTransitioning: false,
            RouteArrived: false,
            PolicyPaused: context.PolicyControlState.IsPolicyPaused,
            RecoveryBlocked: false,
            RiskBlocked: false,
            DestinationLabel: null,
            CurrentLocationLabel: null,
            NextWaypointLabel: null,
            WaypointCount: 0,
            ProgressPercent: null,
            TravelAutopilotActionIntent.None,
            ActionCode: null,
            BehaviorReasonCodes.UnknownBehaviorPack,
            $"Behavior pack '{packName}' is not registered.");

        var memory = TravelAutopilotMemoryState.FromMetadata(context.OperationalMemorySnapshot?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)) with
        {
            BehaviorPackName = packName,
            LastOutcomeCode = BehaviorReasonCodes.UnknownBehaviorPack
        };

        return new TargetBehaviorPlanningResult(packName, "unknown-pack", BehaviorReasonCodes.UnknownBehaviorPack, $"Behavior pack '{packName}' is not registered.", plan, state, memory, [$"Behavior pack '{packName}' is not registered."], metadata);
    }

    private async Task RecordPlanningAsync(SessionId sessionId, TargetBehaviorPlanningResult result, bool isFailure, CancellationToken cancellationToken)
    {
        var stageOutcome = isFailure ? "failed" : "succeeded";
        var activityMetadata = new Dictionary<string, string>(result.Metadata, StringComparer.Ordinal)
        {
            ["planningStateKind"] = result.State.StateKind.ToString(),
            ["decisionPlanStatus"] = result.DecisionPlan.PlanStatus.ToString(),
            ["behaviorPack"] = result.BehaviorPackName ?? string.Empty
        };

        await _observabilityRecorder.RecordActivityAsync(
            sessionId,
            "behavior.pack",
            stageOutcome,
            TimeSpan.Zero,
            result.ReasonCode,
            result.Reason,
            nameof(DefaultTargetBehaviorPackPlanner),
            activityMetadata,
            cancellationToken).ConfigureAwait(false);

        await _observabilityRecorder.RecordDecisionPlanAsync(
            result.DecisionPlan,
            TimeSpan.Zero,
            result.DecisionPlan.PlanStatus.ToString(),
            result.ReasonCode,
            result.Reason,
            nameof(DefaultTargetBehaviorPackPlanner),
            result.Metadata,
            cancellationToken).ConfigureAwait(false);

        foreach (var reason in result.DecisionPlan.Reasons)
        {
            await _observabilityRecorder.RecordDecisionReasonAsync(
                sessionId,
                "behavior.travel",
                reason.Code,
                reason.Message,
                nameof(DefaultTargetBehaviorPackPlanner),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistTravelMemoryAsync(
        SessionId sessionId,
        SessionOperationalMemorySnapshot? currentSnapshot,
        TravelAutopilotMemoryState updatedState,
        CancellationToken cancellationToken)
    {
        if (!_options.OperationalMemory.EnableOperationalMemory)
        {
            return;
        }

        var snapshot = currentSnapshot ?? SessionOperationalMemorySnapshot.Empty(sessionId, _clock.UtcNow);
        var metadata = new Dictionary<string, string>(snapshot.Metadata, StringComparer.Ordinal);
        foreach (var (key, value) in updatedState.ToMetadata())
        {
            metadata[key] = value;
        }

        var updatedSnapshot = snapshot with { Metadata = metadata };
        await _operationalMemoryStore.UpsertAsync(sessionId, updatedSnapshot, [], cancellationToken).ConfigureAwait(false);
    }
}
