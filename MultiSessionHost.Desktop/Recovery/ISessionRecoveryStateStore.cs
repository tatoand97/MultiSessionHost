using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Recovery;

public interface ISessionRecoveryStateStore
{
    ValueTask<SessionRecoverySnapshot> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionRecoverySnapshot>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<SessionRecoveryHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<SessionRecoveryAttemptDecision> TryBeginAttemptAsync(
        SessionId sessionId,
        SessionRecoveryAttemptKind attemptKind,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> RegisterSuccessAsync(
        SessionId sessionId,
        string action,
        string? reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> RegisterFailureAsync(
        SessionId sessionId,
        SessionRecoveryFailureCategory category,
        string action,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkSnapshotStaleAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkSnapshotInvalidatedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkAttachmentInvalidAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkTargetQuarantinedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> ClearQuarantineAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkMetadataDriftDetectedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkMetadataDriftClearedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask<SessionRecoverySnapshot> MarkAdapterHealthAsync(
        SessionId sessionId,
        SessionAdapterHealthState healthState,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RestoreAsync(
        SessionId sessionId,
        SessionRecoverySnapshot? snapshot,
        IReadOnlyList<SessionRecoveryHistoryEntry> history,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}