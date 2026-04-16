using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public interface ISessionScreenSnapshotStore
{
    ValueTask<SessionScreenSnapshot> UpsertLatestAsync(SessionId sessionId, SessionScreenSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask<SessionScreenSnapshot?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionScreenSnapshot>> GetAllLatestAsync(CancellationToken cancellationToken);

    ValueTask<SessionScreenSnapshotSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionScreenSnapshotSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken);

    ValueTask<SessionScreenSnapshotHistory> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
