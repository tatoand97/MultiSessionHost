using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;

namespace MultiSessionHost.Desktop.Persistence;

public sealed class RuntimePersistenceCoordinator : IRuntimePersistenceCoordinator
{
    private sealed class SessionStatusState
    {
        public bool Rehydrated { get; set; }

        public DateTimeOffset? LastLoadedAtUtc { get; set; }

        public DateTimeOffset? LastSavedAtUtc { get; set; }

        public string? LastError { get; set; }

        public string? PersistedPath { get; set; }

        public int ActivityHistoryCount { get; set; }

        public int OperationalMemoryHistoryCount { get; set; }

        public int DecisionPlanHistoryCount { get; set; }

        public int DecisionExecutionHistoryCount { get; set; }

        public int PolicyControlHistoryCount { get; set; }
    }

    private readonly object _gate = new();
    private readonly SessionHostOptions _options;
    private readonly IRuntimePersistenceBackend _backend;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly IClock _clock;
    private readonly ISessionActivityStateStore _activityStateStore;
    private readonly ISessionOperationalMemoryStore _operationalMemoryStore;
    private readonly ISessionDecisionPlanStore _decisionPlanStore;
    private readonly ISessionDecisionPlanExecutionStore _executionStore;
    private readonly ISessionPolicyControlStore _policyControlStore;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly ILogger<RuntimePersistenceCoordinator> _logger;
    private readonly Dictionary<SessionId, SessionStatusState> _statuses = [];

    public RuntimePersistenceCoordinator(
        SessionHostOptions options,
        IRuntimePersistenceBackend backend,
        ISessionRegistry sessionRegistry,
        IClock clock,
        ISessionActivityStateStore activityStateStore,
        ISessionOperationalMemoryStore operationalMemoryStore,
        ISessionDecisionPlanStore decisionPlanStore,
        ISessionDecisionPlanExecutionStore executionStore,
        ISessionPolicyControlStore policyControlStore,
        IObservabilityRecorder observabilityRecorder,
        ILogger<RuntimePersistenceCoordinator> logger)
    {
        _options = options;
        _backend = backend;
        _sessionRegistry = sessionRegistry;
        _clock = clock;
        _activityStateStore = activityStateStore;
        _operationalMemoryStore = operationalMemoryStore;
        _decisionPlanStore = decisionPlanStore;
        _executionStore = executionStore;
        _policyControlStore = policyControlStore;
        _observabilityRecorder = observabilityRecorder;
        _logger = logger;
    }

    public async Task RehydrateAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var startedAt = _clock.UtcNow;
        var configuredSessionIds = _sessionRegistry.GetAll()
            .Select(static definition => definition.Id)
            .ToHashSet();
        var loadResult = await _backend.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        foreach (var error in loadResult.Errors)
        {
            _logger.LogWarning(
                "Runtime persistence load warning for '{Path}': {Message}",
                error.Path,
                error.Message);
        }

        foreach (var envelope in loadResult.Envelopes.OrderBy(static item => item.SessionId.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (!configuredSessionIds.Contains(envelope.SessionId))
            {
                _logger.LogInformation(
                    "Ignoring persisted runtime state for unconfigured session '{SessionId}'.",
                    envelope.SessionId);
                continue;
            }

            try
            {
                await RehydrateEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
                await UpdateStatusAfterLoadAsync(envelope, cancellationToken).ConfigureAwait(false);
                await _observabilityRecorder.RecordPersistenceAsync(envelope.SessionId, "rehydrate", "success", _clock.UtcNow - startedAt, await _backend.GetSessionPathAsync(envelope.SessionId, cancellationToken).ConfigureAwait(false), null, null, null, nameof(RuntimePersistenceCoordinator), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await HandlePersistenceErrorAsync(envelope.SessionId, "rehydrate", exception).ConfigureAwait(false);
                await _observabilityRecorder.RecordPersistenceAsync(envelope.SessionId, "rehydrate", "failure", _clock.UtcNow - startedAt, await _backend.GetSessionPathAsync(envelope.SessionId, cancellationToken).ConfigureAwait(false), null, "rehydrate-failure", exception.Message, nameof(RuntimePersistenceCoordinator), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task FlushSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var startedAt = _clock.UtcNow;
            var envelope = await BuildEnvelopeAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await _backend.SaveSessionAsync(envelope, cancellationToken).ConfigureAwait(false);
            await UpdateStatusAfterSaveAsync(envelope, cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordPersistenceAsync(sessionId, "flush", "success", _clock.UtcNow - startedAt, await _backend.GetSessionPathAsync(sessionId, cancellationToken).ConfigureAwait(false), envelope.DecisionExecutionHistory.Count, null, null, nameof(RuntimePersistenceCoordinator), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandlePersistenceErrorAsync(sessionId, "flush", exception).ConfigureAwait(false);
            await _observabilityRecorder.RecordPersistenceAsync(sessionId, "flush", "failure", TimeSpan.Zero, null, null, "flush-failure", exception.Message, nameof(RuntimePersistenceCoordinator), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (var definition in _sessionRegistry.GetAll().OrderBy(static definition => definition.Id.Value, StringComparer.OrdinalIgnoreCase))
        {
            await FlushSessionAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public RuntimePersistenceStatusSnapshot GetStatus()
    {
        lock (_gate)
        {
            var configuredSessionIds = _sessionRegistry.GetAll()
                .Select(static definition => definition.Id)
                .Concat(_statuses.Keys)
                .Distinct()
                .OrderBy(static sessionId => sessionId.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var sessions = configuredSessionIds
                .Select(
                    sessionId =>
                    {
                        _statuses.TryGetValue(sessionId, out var status);
                        status ??= new SessionStatusState();
                        return new RuntimePersistenceSessionStatus(
                            sessionId,
                            status.Rehydrated,
                            status.LastLoadedAtUtc,
                            status.LastSavedAtUtc,
                            status.LastError,
                            status.PersistedPath,
                            status.ActivityHistoryCount,
                            status.OperationalMemoryHistoryCount,
                            status.DecisionPlanHistoryCount,
                            status.DecisionExecutionHistoryCount,
                            status.PolicyControlHistoryCount);
                    })
                .ToArray();

            return new RuntimePersistenceStatusSnapshot(
                IsEnabled,
                _options.RuntimePersistence.Mode.ToString(),
                _options.RuntimePersistence.BasePath,
                _options.RuntimePersistence.SchemaVersion,
                _clock.UtcNow,
                sessions);
        }
    }

    private bool IsEnabled =>
        _options.RuntimePersistence.EnableRuntimePersistence &&
        _options.RuntimePersistence.Mode != Core.Enums.RuntimePersistenceMode.None;

    private async Task RehydrateEnvelopeAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_options.RuntimePersistence.PersistActivityState && envelope.ActivitySnapshot is not null)
        {
            await _activityStateStore.RestoreAsync(envelope.SessionId, envelope.ActivitySnapshot, cancellationToken).ConfigureAwait(false);
        }

        if (_options.RuntimePersistence.PersistOperationalMemory)
        {
            await _operationalMemoryStore.RestoreAsync(
                envelope.SessionId,
                envelope.OperationalMemorySnapshot,
                envelope.OperationalMemoryHistory,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.RuntimePersistence.PersistDecisionHistory)
        {
            await _decisionPlanStore.RestoreAsync(
                envelope.SessionId,
                envelope.LatestDecisionPlan,
                envelope.DecisionPlanHistory,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.RuntimePersistence.PersistDecisionExecution)
        {
            await _executionStore.RestoreAsync(
                envelope.SessionId,
                envelope.LatestDecisionExecution,
                envelope.DecisionExecutionHistory,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.PolicyControl.PersistPolicyControlState)
        {
            await _policyControlStore.RestoreAsync(
                envelope.SessionId,
                envelope.PolicyControlState,
                envelope.PolicyControlHistory,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SessionRuntimePersistenceEnvelope> BuildEnvelopeAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var activitySnapshot = _options.RuntimePersistence.PersistActivityState
            ? await _activityStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        var memorySnapshot = _options.RuntimePersistence.PersistOperationalMemory
            ? await _operationalMemoryStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        var memoryHistory = _options.RuntimePersistence.PersistOperationalMemory
            ? await _operationalMemoryStore.GetHistoryAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : [];
        var latestPlan = _options.RuntimePersistence.PersistDecisionHistory
            ? await _decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        var decisionHistory = _options.RuntimePersistence.PersistDecisionHistory
            ? await _decisionPlanStore.GetHistoryAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : [];
        var latestExecution = _options.RuntimePersistence.PersistDecisionExecution
            ? await _executionStore.GetCurrentAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        var executionHistory = _options.RuntimePersistence.PersistDecisionExecution
            ? await _executionStore.GetHistoryAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : [];
        var policyControlState = _options.PolicyControl.PersistPolicyControlState
            ? await _policyControlStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
        var policyControlHistory = _options.PolicyControl.PersistPolicyControlState
            ? await _policyControlStore.GetHistoryAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : [];

        return new SessionRuntimePersistenceEnvelope(
            _options.RuntimePersistence.SchemaVersion,
            sessionId,
            _clock.UtcNow,
            activitySnapshot,
            memorySnapshot,
            memoryHistory,
            latestPlan,
            decisionHistory,
            latestExecution,
            executionHistory,
            policyControlState,
            policyControlHistory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "runtime-persistence",
                ["persistedBy"] = nameof(RuntimePersistenceCoordinator)
            });
    }

    private async Task UpdateStatusAfterLoadAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken)
    {
        var path = await _backend.GetSessionPathAsync(envelope.SessionId, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var status = GetOrCreateStatusUnsafe(envelope.SessionId);
            status.Rehydrated = true;
            status.LastLoadedAtUtc = _clock.UtcNow;
            status.LastError = null;
            status.PersistedPath = path;
            status.ActivityHistoryCount = envelope.ActivitySnapshot?.History.Count ?? 0;
            status.OperationalMemoryHistoryCount = envelope.OperationalMemoryHistory.Count;
            status.DecisionPlanHistoryCount = envelope.DecisionPlanHistory.Count;
            status.DecisionExecutionHistoryCount = envelope.DecisionExecutionHistory.Count;
            status.PolicyControlHistoryCount = envelope.PolicyControlHistory.Count;
        }
    }

    private async Task UpdateStatusAfterSaveAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken)
    {
        var path = await _backend.GetSessionPathAsync(envelope.SessionId, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var status = GetOrCreateStatusUnsafe(envelope.SessionId);
            status.LastSavedAtUtc = envelope.SavedAtUtc;
            status.LastError = null;
            status.PersistedPath = path;
            status.ActivityHistoryCount = envelope.ActivitySnapshot?.History.Count ?? 0;
            status.OperationalMemoryHistoryCount = envelope.OperationalMemoryHistory.Count;
            status.DecisionPlanHistoryCount = envelope.DecisionPlanHistory.Count;
            status.DecisionExecutionHistoryCount = envelope.DecisionExecutionHistory.Count;
            status.PolicyControlHistoryCount = envelope.PolicyControlHistory.Count;
        }
    }

    private Task HandlePersistenceErrorAsync(SessionId sessionId, string operation, Exception exception)
    {
        lock (_gate)
        {
            GetOrCreateStatusUnsafe(sessionId).LastError = exception.Message;
        }

        _logger.LogWarning(
            exception,
            "Runtime persistence {Operation} failed for session '{SessionId}'.",
            operation,
            sessionId);

        if (_options.RuntimePersistence.FailOnPersistenceErrors)
        {
            throw new InvalidOperationException(
                $"Runtime persistence {operation} failed for session '{sessionId}'.",
                exception);
        }

        return Task.CompletedTask;
    }

    private SessionStatusState GetOrCreateStatusUnsafe(SessionId sessionId)
    {
        if (_statuses.TryGetValue(sessionId, out var status))
        {
            return status;
        }

        status = new SessionStatusState();
        _statuses[sessionId] = status;
        return status;
    }
}
