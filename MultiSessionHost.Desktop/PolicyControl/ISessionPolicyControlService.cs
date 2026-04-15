using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.PolicyControl;

public interface ISessionPolicyControlService
{
    ValueTask<SessionPolicyControlState> GetStateAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionPolicyControlState>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<SessionPolicyControlHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<PolicyControlActionResult> PauseAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken);

    ValueTask<PolicyControlActionResult> ResumeAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken);

    ValueTask<PolicyEvaluationGateResult> GetEvaluationGateAsync(SessionId sessionId, CancellationToken cancellationToken);
}