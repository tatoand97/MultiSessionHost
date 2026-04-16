namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionRecoverySnapshotDto(
    string SessionId,
    string RecoveryStatus,
    string CircuitBreakerState,
    int ConsecutiveFailureCount,
    IReadOnlyDictionary<string, int> FailureCountsByCategory,
    DateTimeOffset? LastFailureAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? BackoffUntilUtc,
    DateTimeOffset? NextRecoveryAttemptAtUtc,
    bool IsSnapshotStale,
    bool IsAttachmentInvalid,
    bool IsTargetQuarantined,
    string? TargetQuarantineReasonCode,
    bool MetadataDriftDetected,
    string AdapterHealthState,
    string? LastRecoveryAction,
    string? LastRecoveryReasonCode,
    string? LastRecoveryReason,
    DateTimeOffset? LastTransitionAtUtc,
    bool IsBlockedFromRecoveryAttempts,
    int HalfOpenProbeAttempts,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionRecoveryHistoryEntryDto(
    string SessionId,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string RecoveryStatus,
    string CircuitBreakerState,
    string AdapterHealthState,
    string? FailureCategory,
    string? ReasonCode,
    string? Reason,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionRecoveryStatusDto(
    SessionRecoverySnapshotDto Current,
    IReadOnlyCollection<SessionRecoveryHistoryEntryDto> History);