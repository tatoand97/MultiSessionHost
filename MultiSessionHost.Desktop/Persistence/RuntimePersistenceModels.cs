using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.Persistence;

public sealed record SessionRuntimePersistenceEnvelope(
    int SchemaVersion,
    SessionId SessionId,
    DateTimeOffset SavedAtUtc,
    SessionActivitySnapshot? ActivitySnapshot,
    SessionOperationalMemorySnapshot? OperationalMemorySnapshot,
    IReadOnlyList<MemoryObservationRecord> OperationalMemoryHistory,
    DecisionPlan? LatestDecisionPlan,
    IReadOnlyList<DecisionPlanHistoryEntry> DecisionPlanHistory,
    DecisionPlanExecutionResult? LatestDecisionExecution,
    IReadOnlyList<DecisionPlanExecutionRecord> DecisionExecutionHistory,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RuntimePersistenceSessionStatus(
    SessionId SessionId,
    bool Rehydrated,
    DateTimeOffset? LastLoadedAtUtc,
    DateTimeOffset? LastSavedAtUtc,
    string? LastError,
    string? PersistedPath,
    int ActivityHistoryCount,
    int OperationalMemoryHistoryCount,
    int DecisionPlanHistoryCount,
    int DecisionExecutionHistoryCount);

public sealed record RuntimePersistenceStatusSnapshot(
    bool Enabled,
    string Mode,
    string? BasePath,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimePersistenceSessionStatus> Sessions);

public sealed record RuntimePersistenceLoadResult(
    IReadOnlyList<SessionRuntimePersistenceEnvelope> Envelopes,
    IReadOnlyList<RuntimePersistenceLoadError> Errors);

public sealed record RuntimePersistenceLoadError(
    string? SessionId,
    string? Path,
    string Message);
