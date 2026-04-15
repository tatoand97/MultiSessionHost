using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Observability;

public interface ISessionObservabilityStore
{
    ValueTask<SessionObservabilitySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<SessionObservabilityMetricsSnapshot?> GetMetricsAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionObservabilitySnapshot>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionObservabilitySummary>> GetSummariesAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionObservabilityEvent>> GetEventsAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<AdapterErrorRecord>> GetErrorsAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<GlobalObservabilitySnapshot> GetGlobalSnapshotAsync(CancellationToken cancellationToken);

    ValueTask RecordAsync(SessionObservabilityEvent sessionEvent, CancellationToken cancellationToken);

    ValueTask RecordErrorAsync(AdapterErrorRecord errorRecord, CancellationToken cancellationToken);
}