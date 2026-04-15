using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.PolicyControl;

public interface ISessionPolicyControlStore
{
    ValueTask<SessionPolicyControlState> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionPolicyControlState>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<SessionPolicyControlHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<PolicyControlActionResult> PauseAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken);

    ValueTask<PolicyControlActionResult> ResumeAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken);

    ValueTask RestoreAsync(
        SessionId sessionId,
        SessionPolicyControlState? state,
        IReadOnlyList<SessionPolicyControlHistoryEntry> history,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}