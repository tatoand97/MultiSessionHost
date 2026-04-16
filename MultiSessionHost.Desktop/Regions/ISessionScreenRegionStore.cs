using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Regions;

public interface ISessionScreenRegionStore
{
    ValueTask<SessionScreenRegionResolution> UpsertLatestAsync(SessionId sessionId, SessionScreenRegionResolution resolution, CancellationToken cancellationToken);

    ValueTask<SessionScreenRegionResolution?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionScreenRegionResolution>> GetAllLatestAsync(CancellationToken cancellationToken);

    ValueTask<SessionScreenRegionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionScreenRegionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken);
}