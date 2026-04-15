using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.PolicyControl;

public sealed class DefaultSessionPolicyControlService : ISessionPolicyControlService, IPolicyControlGate
{
    private readonly ISessionPolicyControlStore _store;

    public DefaultSessionPolicyControlService(ISessionPolicyControlStore store)
    {
        _store = store;
    }

    public ValueTask<SessionPolicyControlState> GetStateAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _store.GetAsync(sessionId, cancellationToken);

    public ValueTask<IReadOnlyCollection<SessionPolicyControlState>> GetAllAsync(CancellationToken cancellationToken) =>
        _store.GetAllAsync(cancellationToken);

    public ValueTask<IReadOnlyList<SessionPolicyControlHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _store.GetHistoryAsync(sessionId, cancellationToken);

    public ValueTask<PolicyControlActionResult> PauseAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken) =>
        _store.PauseAsync(sessionId, request, cancellationToken);

    public ValueTask<PolicyControlActionResult> ResumeAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken) =>
        _store.ResumeAsync(sessionId, request, cancellationToken);

    public async ValueTask<PolicyEvaluationGateResult> GetEvaluationGateAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var state = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return new PolicyEvaluationGateResult(
            sessionId,
            state.IsPolicyPaused,
            state,
            state.ReasonCode,
            state.Reason,
            state.ChangedBy,
            state.Metadata);
    }
}