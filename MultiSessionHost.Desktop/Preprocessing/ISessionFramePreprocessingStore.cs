using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public interface ISessionFramePreprocessingStore
{
    ValueTask<SessionFramePreprocessingResult> UpsertLatestAsync(SessionId sessionId, SessionFramePreprocessingResult result, CancellationToken cancellationToken);

    ValueTask<SessionFramePreprocessingResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionFramePreprocessingResult>> GetAllLatestAsync(CancellationToken cancellationToken);

    ValueTask<SessionFramePreprocessingSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionFramePreprocessingSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken);
}
