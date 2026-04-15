using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionDomainStateStore
{
    ValueTask InitializeAsync(SessionDomainState state, CancellationToken cancellationToken);

    ValueTask<SessionDomainState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<SessionDomainState>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<SessionDomainState> UpdateAsync(
        SessionId sessionId,
        Func<SessionDomainState, SessionDomainState> update,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
