using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Templates;

public interface ISessionTemplateDetectionStore
{
    ValueTask<SessionTemplateDetectionResult> UpsertLatestAsync(SessionId sessionId, SessionTemplateDetectionResult result, CancellationToken cancellationToken);

    ValueTask<SessionTemplateDetectionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionTemplateDetectionResult>> GetAllLatestAsync(CancellationToken cancellationToken);

    ValueTask<SessionTemplateDetectionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionTemplateDetectionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken);
}
