using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class DefaultSessionUiRefreshService : ISessionUiRefreshService
{
    private readonly SessionHostOptions _options;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
    private readonly IUiSnapshotSerializer _uiSnapshotSerializer;
    private readonly IUiTreeNormalizerResolver _uiTreeNormalizerResolver;
    private readonly IUiStateProjector _uiStateProjector;
    private readonly IWorkItemPlannerResolver _workItemPlannerResolver;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly ISessionDomainStateStore _sessionDomainStateStore;
    private readonly ISessionDomainStateProjectionService _domainStateProjectionService;
    private readonly IUiSemanticExtractionPipeline _semanticExtractionPipeline;
    private readonly ISessionSemanticExtractionStore _semanticExtractionStore;
    private readonly IRiskClassificationPipeline _riskClassificationPipeline;
    private readonly IPolicyEngine _policyEngine;
    private readonly ISessionDecisionPlanStore _sessionDecisionPlanStore;
    private readonly ISessionRiskAssessmentStore _sessionRiskAssessmentStore;
    private readonly ISessionActivityStateEvaluator _activityStateEvaluator;
    private readonly ISessionActivityStateStore _activityStateStore;
    private readonly ISessionOperationalMemoryStore _operationalMemoryStore;
    private readonly ISessionOperationalMemoryUpdater _operationalMemoryUpdater;
    private readonly ITargetBehaviorPackPlanner _targetBehaviorPackPlanner;
    private readonly IRuntimePersistenceCoordinator _runtimePersistenceCoordinator;
    private readonly ISessionRecoveryStateStore _recoveryStateStore;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly IServiceProvider _serviceProvider;
    private readonly IClock _clock;
    private readonly ILogger<DefaultSessionUiRefreshService> _logger;

    public DefaultSessionUiRefreshService(
        SessionHostOptions options,
        IDesktopTargetAdapterRegistry adapterRegistry,
        IUiSnapshotSerializer uiSnapshotSerializer,
        IUiTreeNormalizerResolver uiTreeNormalizerResolver,
        IUiStateProjector uiStateProjector,
        IWorkItemPlannerResolver workItemPlannerResolver,
        ISessionUiStateStore sessionUiStateStore,
        ISessionDomainStateStore sessionDomainStateStore,
        ISessionDomainStateProjectionService domainStateProjectionService,
        IUiSemanticExtractionPipeline semanticExtractionPipeline,
        ISessionSemanticExtractionStore semanticExtractionStore,
        IRiskClassificationPipeline riskClassificationPipeline,
        IPolicyEngine policyEngine,
        ISessionDecisionPlanStore sessionDecisionPlanStore,
        ISessionRiskAssessmentStore sessionRiskAssessmentStore,
        ISessionActivityStateEvaluator activityStateEvaluator,
        ISessionActivityStateStore activityStateStore,
        ISessionOperationalMemoryStore operationalMemoryStore,
        ISessionOperationalMemoryUpdater operationalMemoryUpdater,
        ITargetBehaviorPackPlanner targetBehaviorPackPlanner,
        IRuntimePersistenceCoordinator runtimePersistenceCoordinator,
        ISessionRecoveryStateStore recoveryStateStore,
        IObservabilityRecorder observabilityRecorder,
        IServiceProvider serviceProvider,
        IClock clock,
        ILogger<DefaultSessionUiRefreshService> logger)
    {
        _options = options;
        _adapterRegistry = adapterRegistry;
        _uiSnapshotSerializer = uiSnapshotSerializer;
        _uiTreeNormalizerResolver = uiTreeNormalizerResolver;
        _uiStateProjector = uiStateProjector;
        _workItemPlannerResolver = workItemPlannerResolver;
        _sessionUiStateStore = sessionUiStateStore;
        _sessionDomainStateStore = sessionDomainStateStore;
        _domainStateProjectionService = domainStateProjectionService;
        _semanticExtractionPipeline = semanticExtractionPipeline;
        _semanticExtractionStore = semanticExtractionStore;
        _riskClassificationPipeline = riskClassificationPipeline;
        _policyEngine = policyEngine;
        _sessionDecisionPlanStore = sessionDecisionPlanStore;
        _sessionRiskAssessmentStore = sessionRiskAssessmentStore;
        _activityStateEvaluator = activityStateEvaluator;
        _activityStateStore = activityStateStore;
        _operationalMemoryStore = operationalMemoryStore;
        _operationalMemoryUpdater = operationalMemoryUpdater;
        _targetBehaviorPackPlanner = targetBehaviorPackPlanner;
        _runtimePersistenceCoordinator = runtimePersistenceCoordinator;
        _recoveryStateStore = recoveryStateStore;
        _observabilityRecorder = observabilityRecorder;
        _serviceProvider = serviceProvider;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SessionUiState> CaptureAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        EnsureUiSnapshotsEnabled();

        try
        {
            var captureStart = System.Diagnostics.Stopwatch.StartNew();
            var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
            var uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
            var rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);
            captureStart.Stop();

            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "ui.snapshot",
                SessionObservabilityOutcome.Success.ToString(),
                captureStart.Elapsed,
                null,
                null,
                nameof(DefaultSessionUiRefreshService),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["adapterKind"] = context.Profile.Kind.ToString()
                },
                cancellationToken).ConfigureAwait(false);

            await _recoveryStateStore.RegisterSuccessAsync(snapshot.SessionId, "ui-capture", "recovery.success_cleared_failures", "UI snapshot captured successfully.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);

            return await _sessionUiStateStore.UpdateAsync(
                snapshot.SessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            await _recoveryStateStore.RegisterFailureAsync(snapshot.SessionId, SessionRecoveryFailureCategory.SnapshotCaptureFailed, "ui-capture-failed", "recovery.snapshot.capture.failed", exception.Message, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SessionUiState> ProjectAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        CancellationToken cancellationToken)
    {
        EnsureUiSnapshotsEnabled();

        try
        {
            var projectionStart = System.Diagnostics.Stopwatch.StartNew();
            var uiState = await _sessionUiStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"UI state for session '{snapshot.SessionId}' was not initialized.");
            var recoverySnapshot = await _recoveryStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
            var forceCapture = IsSnapshotStale(uiState, recoverySnapshot) || recoverySnapshot.IsAttachmentInvalid || recoverySnapshot.IsTargetQuarantined || recoverySnapshot.MetadataDriftDetected || string.IsNullOrWhiteSpace(uiState.RawSnapshotJson);

            if (IsSnapshotStale(uiState, recoverySnapshot))
            {
                await _recoveryStateStore.MarkSnapshotStaleAsync(snapshot.SessionId, "recovery.snapshot.stale_detected", "The UI snapshot is stale and must be refreshed.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
                uiState = await _sessionUiStateStore.UpdateAsync(
                    snapshot.SessionId,
                    current => current with
                    {
                        RawSnapshotJson = null,
                        ProjectedTree = null,
                        LastDiff = null,
                        PlannedWorkItems = [],
                        LastRefreshError = "The UI snapshot is stale and was invalidated.",
                        LastRefreshErrorAtUtc = _clock.UtcNow
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            UiSnapshotEnvelope uiSnapshot;
            string rawJson;

            if (forceCapture)
            {
                if (attachment is null)
                {
                    throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a usable UI snapshot or attachment to project.");
                }

                var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
                uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
                rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);

                uiState = await _sessionUiStateStore.UpdateAsync(
                    snapshot.SessionId,
                    current => current with
                    {
                        RawSnapshotJson = rawJson,
                        LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
                {
                    throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a raw UI snapshot to project.");
                }

                rawJson = uiState.RawSnapshotJson;
                uiSnapshot = _uiSnapshotSerializer.Deserialize(rawJson);
            }

            var source = DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.UiSource, context.Profile.ProfileName);
            var metadata = new UiSnapshotMetadata(
                snapshot.SessionId.Value,
                source,
                uiSnapshot.CapturedAtUtc,
                uiSnapshot.Process.ProcessId,
                uiSnapshot.Window.WindowHandle,
                uiSnapshot.Window.Title,
                uiSnapshot.Metadata);
            var treeNormalizer = _uiTreeNormalizerResolver.Resolve(context);
            var planner = _workItemPlannerResolver.Resolve(context);
            var tree = treeNormalizer.Normalize(metadata, uiSnapshot.Root);
            var diff = _uiStateProjector.Project(uiState.ProjectedTree, tree);
            var plannedWorkItems = planner.Plan(tree);

            var projectedUiState = await _sessionUiStateStore.UpdateAsync(
                snapshot.SessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc,
                    ProjectedTree = tree,
                    LastDiff = diff,
                    PlannedWorkItems = plannedWorkItems,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);

            var currentDomainState = await _sessionDomainStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Domain state for session '{snapshot.SessionId}' was not initialized.");
            var now = _clock.UtcNow;
            var semanticContext = new UiSemanticExtractionContext(
                snapshot.SessionId,
                projectedUiState,
                tree,
                currentDomainState,
                snapshot,
                context,
                attachment,
                now);
            var semanticExtraction = await _semanticExtractionPipeline.ExtractAsync(semanticContext, cancellationToken).ConfigureAwait(false);
            await _semanticExtractionStore.UpdateAsync(snapshot.SessionId, semanticExtraction, cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "semantic.extraction",
                SessionObservabilityOutcome.Success.ToString(),
                TimeSpan.Zero,
                null,
                null,
                nameof(DefaultSessionUiRefreshService),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pipeline"] = nameof(IUiSemanticExtractionPipeline)
                },
                cancellationToken).ConfigureAwait(false);
            var riskAssessment = await _riskClassificationPipeline.AssessAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "risk.classification",
                SessionObservabilityOutcome.Success.ToString(),
                TimeSpan.Zero,
                null,
                null,
                nameof(DefaultSessionUiRefreshService),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["pipeline"] = nameof(IRiskClassificationPipeline)
                },
                cancellationToken).ConfigureAwait(false);

            await _sessionDomainStateStore.UpdateAsync(
                snapshot.SessionId,
                current => _domainStateProjectionService.Project(
                    current,
                    snapshot,
                    context,
                    projectedUiState,
                    attachment,
                    semanticExtraction,
                    riskAssessment,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
                projectionStart.Stop();
                await _observabilityRecorder.RecordActivityAsync(
                    snapshot.SessionId,
                    "domain.projection",
                    SessionObservabilityOutcome.Success.ToString(),
                    projectionStart.Elapsed,
                    null,
                    null,
                    nameof(DefaultSessionUiRefreshService),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    cancellationToken).ConfigureAwait(false);

            await _policyEngine.EvaluateAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

            _ = await _targetBehaviorPackPlanner.TryPlanAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

            var updatedDomainState = await _sessionDomainStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Domain state for session '{snapshot.SessionId}' was not refreshed.");
            var currentDecisionPlan = await _sessionDecisionPlanStore.GetLatestAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Decision plan for session '{snapshot.SessionId}' was not generated.");
            var currentRiskAssessment = await _sessionRiskAssessmentStore.GetLatestAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
            var previousActivitySnapshot = await _activityStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

            var activityEvaluationContext = new SessionActivityEvaluationContext(
                snapshot.SessionId,
                updatedDomainState,
                currentDecisionPlan,
                currentRiskAssessment,
                previousActivitySnapshot,
                recoverySnapshot,
                _clock.UtcNow);

            var activityEvaluationResult = await _activityStateEvaluator.EvaluateAsync(activityEvaluationContext, cancellationToken).ConfigureAwait(false);
            await _activityStateStore.UpsertAsync(snapshot.SessionId, activityEvaluationResult.NewSnapshot, cancellationToken).ConfigureAwait(false);

            DecisionPlanExecutionResult? executionResult = null;

            if (_options.DecisionExecution.EnableDecisionExecution && _options.DecisionExecution.AutoExecuteAfterEvaluation)
            {
                var executionContext = new DecisionPlanExecutionContext(
                    snapshot.SessionId,
                    currentDecisionPlan,
                    updatedDomainState,
                    currentRiskAssessment,
                    activityEvaluationResult.NewSnapshot,
                    _clock.UtcNow,
                    WasAutoExecuted: true);
                var decisionPlanExecutor = _serviceProvider.GetRequiredService<IDecisionPlanExecutor>();
                executionResult = await decisionPlanExecutor.ExecuteAsync(executionContext, cancellationToken).ConfigureAwait(false);
            }

            await UpdateOperationalMemoryAsync(
                snapshot.SessionId,
                updatedDomainState,
                semanticExtraction,
                currentRiskAssessment,
                currentDecisionPlan,
                executionResult,
                activityEvaluationResult.NewSnapshot,
                cancellationToken).ConfigureAwait(false);

            var flushStart = System.Diagnostics.Stopwatch.StartNew();
            await FlushIfEnabledAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
            flushStart.Stop();
            await _observabilityRecorder.RecordPersistenceAsync(
                snapshot.SessionId,
                "flush",
                SessionObservabilityOutcome.Success.ToString(),
                flushStart.Elapsed,
                null,
                null,
                null,
                null,
                nameof(DefaultSessionUiRefreshService),
                new Dictionary<string, string>(StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);

            await _recoveryStateStore.RegisterSuccessAsync(snapshot.SessionId, "ui-refresh", "recovery.success_cleared_failures", "UI refresh completed successfully.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);

            return await _sessionUiStateStore.UpdateAsync(
                snapshot.SessionId,
                current => current with
                {
                    LastRefreshCompletedAtUtc = _clock.UtcNow,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            await RecordDomainRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            await _recoveryStateStore.RegisterFailureAsync(snapshot.SessionId, SessionRecoveryFailureCategory.RefreshProjectionFailure, "ui-refresh-failed", "recovery.refresh_projection.failed", exception.Message, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(snapshot.SessionId, context.Profile.ProfileName, "ui-refresh", exception, "ui-refresh-failure", nameof(DefaultSessionUiRefreshService), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private Task FlushIfEnabledAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _options.RuntimePersistence.AutoFlushAfterStateChanges
            ? _runtimePersistenceCoordinator.FlushSessionAsync(sessionId, cancellationToken)
            : Task.CompletedTask;

    public async Task<SessionUiState> RefreshAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        await CaptureAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        return await ProjectAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordUiRefreshErrorAsync(SessionId sessionId, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "UI refresh failed for session '{SessionId}'.", sessionId);

        await _sessionUiStateStore.UpdateAsync(
            sessionId,
            current => current with
            {
                LastRefreshError = exception.Message,
                LastRefreshErrorAtUtc = _clock.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordDomainRefreshErrorAsync(SessionId sessionId, Exception exception, CancellationToken cancellationToken)
    {
        await _sessionDomainStateStore.UpdateAsync(
            sessionId,
            current => _domainStateProjectionService.ProjectRefreshFailure(current, exception, _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    private void EnsureUiSnapshotsEnabled()
    {
        if (!_options.EnableUiSnapshots)
        {
            throw new InvalidOperationException("UI snapshots are disabled. Set EnableUiSnapshots=true to request raw or projected UI state.");
        }
    }

    private bool IsSnapshotStale(SessionUiState uiState, SessionRecoverySnapshot recoverySnapshot)
    {
        if (uiState.LastSnapshotCapturedAtUtc is null)
        {
            return false;
        }

        if (_options.Recovery.SnapshotStaleAfterMs <= 0)
        {
            return false;
        }

        var staleAfter = uiState.LastSnapshotCapturedAtUtc.Value.AddMilliseconds(_options.Recovery.SnapshotStaleAfterMs);
        return recoverySnapshot.IsSnapshotStale || _clock.UtcNow >= staleAfter;
    }

    private async ValueTask UpdateOperationalMemoryAsync(
        SessionId sessionId,
        SessionDomainState domainState,
        UiSemanticExtractionResult semanticExtraction,
        RiskAssessmentResult? riskAssessment,
        DecisionPlan decisionPlan,
        DecisionPlanExecutionResult? executionResult,
        SessionActivitySnapshot activitySnapshot,
        CancellationToken cancellationToken)
    {
        var previousMemory = await _operationalMemoryStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var memoryStart = System.Diagnostics.Stopwatch.StartNew();
        var memoryResult = await _operationalMemoryUpdater.UpdateAsync(
            new SessionOperationalMemoryUpdateContext(
                sessionId,
                previousMemory,
                domainState,
                semanticExtraction,
                riskAssessment,
                decisionPlan,
                executionResult,
                activitySnapshot,
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
            memoryStart.Stop();

            await _observabilityRecorder.RecordActivityAsync(
                sessionId,
                "memory.update",
                memoryResult.Snapshot is null ? SessionObservabilityOutcome.Skipped.ToString() : SessionObservabilityOutcome.Success.ToString(),
                memoryStart.Elapsed,
                memoryResult.Snapshot is null ? "memory-update-skipped" : null,
                memoryResult.Snapshot is null ? "Operational memory update did not produce a snapshot." : null,
                nameof(DefaultSessionUiRefreshService),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["observationCount"] = memoryResult.AddedObservationRecords.Count.ToString()
                },
                cancellationToken).ConfigureAwait(false);

        if (memoryResult.Snapshot is not null)
        {
            await _operationalMemoryStore.UpsertAsync(
                sessionId,
                memoryResult.Snapshot,
                memoryResult.AddedObservationRecords,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
