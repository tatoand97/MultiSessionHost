using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Risk;

public interface ISessionRiskAssessmentStore
{
    ValueTask InitializeAsync(SessionId sessionId, RiskAssessmentResult result, CancellationToken cancellationToken);

    ValueTask<RiskAssessmentResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<RiskAssessmentResult>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<RiskAssessmentResult> UpdateAsync(SessionId sessionId, RiskAssessmentResult result, CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
