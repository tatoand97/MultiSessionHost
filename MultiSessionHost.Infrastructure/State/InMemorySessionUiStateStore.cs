using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.State;

public sealed class InMemorySessionUiStateStore : ISessionUiStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionUiState> _states = [];

    public ValueTask InitializeAsync(SessionUiState state, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_states.ContainsKey(state.SessionId))
            {
                throw new InvalidOperationException($"Session UI state '{state.SessionId}' is already initialized.");
            }

            _states[state.SessionId] = state;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SessionUiState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state : null);
        }
    }

    public IReadOnlyCollection<SessionUiState> GetAll()
    {
        lock (_gate)
        {
            return _states.Values.ToArray();
        }
    }

    public ValueTask<SessionUiState> SetAsync(SessionUiState state, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _states[state.SessionId] = state;
            return ValueTask.FromResult(state);
        }
    }

    public ValueTask<SessionUiState> UpdateAsync(
        SessionId sessionId,
        Func<SessionUiState, SessionUiState> update,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(sessionId, out var current))
            {
                throw new InvalidOperationException($"Session UI state '{sessionId}' is not initialized.");
            }

            var updated = update(current);
            _states[sessionId] = updated;
            return ValueTask.FromResult(updated);
        }
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _states.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }
}
