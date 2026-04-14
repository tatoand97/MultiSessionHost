using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionUiStateStore
{
    ValueTask InitializeAsync(SessionUiState state, CancellationToken cancellationToken);

    ValueTask<SessionUiState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    IReadOnlyCollection<SessionUiState> GetAll();

    ValueTask<SessionUiState> SetAsync(SessionUiState state, CancellationToken cancellationToken);

    ValueTask<SessionUiState> UpdateAsync(
        SessionId sessionId,
        Func<SessionUiState, SessionUiState> update,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
