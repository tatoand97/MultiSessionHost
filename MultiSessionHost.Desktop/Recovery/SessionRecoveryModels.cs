using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Recovery;

public enum SessionRecoveryStatus
{
    Healthy = 0,
    Recovering = 1,
    Backoff = 2,
    CircuitOpen = 3,
    HalfOpen = 4,
    Quarantined = 5,
    Exhausted = 6,
    Faulted = 7
}

public enum SessionRecoveryCircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}

public enum SessionAdapterHealthState
{
    Healthy = 0,
    Degraded = 1,
    Exhausted = 2
}

public enum SessionRecoveryFailureCategory
{
    AttachmentEnsureFailed = 0,
    AttachmentLost = 1,
    SnapshotCaptureFailed = 2,
    SnapshotStale = 3,
    TargetInvalid = 4,
    MetadataDrift = 5,
    AdapterTransientFailure = 6,
    AdapterDegraded = 7,
    AdapterExhausted = 8,
    RefreshProjectionFailure = 9,
    CommandExecutionFailure = 10
}

public enum SessionRecoveryAttemptKind
{
    AttachmentEnsure = 0,
    SnapshotCapture = 1,
    Projection = 2,
    Refresh = 3,
    WorkItem = 4,
    CommandExecution = 5,
    MetadataValidation = 6
}

public sealed record SessionRecoverySnapshot(
    SessionId SessionId,
    SessionRecoveryStatus RecoveryStatus,
    SessionRecoveryCircuitState CircuitBreakerState,
    int ConsecutiveFailureCount,
    IReadOnlyDictionary<SessionRecoveryFailureCategory, int> FailureCountsByCategory,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? BackoffUntilUtc,
    DateTimeOffset? NextRecoveryAttemptAtUtc,
    bool IsSnapshotStale,
    bool IsAttachmentInvalid,
    bool IsTargetQuarantined,
    string? TargetQuarantineReasonCode,
    bool MetadataDriftDetected,
    SessionAdapterHealthState AdapterHealthState,
    string? LastRecoveryAction,
    string? LastRecoveryReasonCode,
    string? LastRecoveryReason,
    DateTimeOffset? LastTransitionAtUtc,
    bool IsBlockedFromRecoveryAttempts,
    int HalfOpenProbeAttempts,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static SessionRecoverySnapshot Create(SessionId sessionId) =>
        new(
            sessionId,
            SessionRecoveryStatus.Healthy,
            SessionRecoveryCircuitState.Closed,
            0,
            new Dictionary<SessionRecoveryFailureCategory, int>(),
            null,
            null,
            null,
            null,
            false,
            false,
            false,
            null,
            false,
            SessionAdapterHealthState.Healthy,
            null,
            null,
            null,
            null,
            false,
            0,
            new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record SessionRecoveryHistoryEntry(
    SessionId SessionId,
    DateTimeOffset OccurredAtUtc,
    string Action,
    SessionRecoveryStatus RecoveryStatus,
    SessionRecoveryCircuitState CircuitBreakerState,
    SessionAdapterHealthState AdapterHealthState,
    SessionRecoveryFailureCategory? FailureCategory,
    string? ReasonCode,
    string? Reason,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionRecoveryAttemptDecision(
    SessionRecoverySnapshot Snapshot,
    bool CanAttempt,
    bool IsProbe,
    string? ReasonCode,
    string? Reason,
    DateTimeOffset? NextEligibleAtUtc);