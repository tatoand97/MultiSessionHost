using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionStateStore
{
    ValueTask InitializeAsync(SessionRuntimeState state, CancellationToken cancellationToken);

    ValueTask<SessionRuntimeState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    IReadOnlyCollection<SessionRuntimeState> GetAll();

    ValueTask<SessionRuntimeState> SetAsync(SessionRuntimeState state, CancellationToken cancellationToken);

    ValueTask<SessionRuntimeState> UpdateAsync(
        SessionId sessionId,
        Func<SessionRuntimeState, SessionRuntimeState> update,
        CancellationToken cancellationToken);
}
