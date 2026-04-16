using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Ocr;

public interface ISessionOcrExtractionStore
{
    ValueTask<SessionOcrExtractionResult> UpsertLatestAsync(SessionId sessionId, SessionOcrExtractionResult result, CancellationToken cancellationToken);

    ValueTask<SessionOcrExtractionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionOcrExtractionResult>> GetAllLatestAsync(CancellationToken cancellationToken);

    ValueTask<SessionOcrExtractionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionOcrExtractionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken);
}
