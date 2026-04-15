using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Risk;

public interface IRiskClassificationPipeline
{
    ValueTask<RiskAssessmentResult> AssessAsync(SessionId sessionId, CancellationToken cancellationToken);
}
