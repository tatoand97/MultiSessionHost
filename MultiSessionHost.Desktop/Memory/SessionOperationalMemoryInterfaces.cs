using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Memory;

public interface ISessionOperationalMemoryReader
{
    ValueTask<SessionOperationalMemorySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionOperationalMemorySnapshot>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MemoryObservationRecord>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<WorksiteObservation>> GetKnownWorksitesAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<SessionOperationalMemorySummary?> GetSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<WorksiteObservation?> GetLatestWorksiteObservationAsync(SessionId sessionId, string worksiteKey, CancellationToken cancellationToken);
}

public interface ISessionOperationalMemoryStore : ISessionOperationalMemoryReader
{
    SessionOperationalMemorySnapshot? GetCurrent(SessionId sessionId);

    IReadOnlyCollection<SessionOperationalMemorySnapshot> GetAllCurrent();

    ValueTask InitializeIfMissingAsync(SessionId sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    ValueTask UpsertAsync(
        SessionId sessionId,
        SessionOperationalMemorySnapshot snapshot,
        IReadOnlyList<MemoryObservationRecord> newObservationRecords,
        CancellationToken cancellationToken);

    ValueTask RestoreAsync(
        SessionId sessionId,
        SessionOperationalMemorySnapshot? snapshot,
        IReadOnlyList<MemoryObservationRecord> history,
        CancellationToken cancellationToken);
}

public interface ISessionOperationalMemoryUpdater
{
    ValueTask<SessionOperationalMemoryUpdateResult> UpdateAsync(
        SessionOperationalMemoryUpdateContext context,
        CancellationToken cancellationToken);
}
