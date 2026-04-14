using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.State;

public sealed class InMemorySessionStateStore : ISessionStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionRuntimeState> _states = [];

    public ValueTask InitializeAsync(SessionRuntimeState state, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_states.ContainsKey(state.SessionId))
            {
                throw new InvalidOperationException($"Session runtime state '{state.SessionId}' is already initialized.");
            }

            _states[state.SessionId] = state;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SessionRuntimeState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state : null);
        }
    }

    public IReadOnlyCollection<SessionRuntimeState> GetAll()
    {
        lock (_gate)
        {
            return _states.Values.ToArray();
        }
    }

    public ValueTask<SessionRuntimeState> SetAsync(SessionRuntimeState state, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _states[state.SessionId] = state;
            return ValueTask.FromResult(state);
        }
    }

    public ValueTask<SessionRuntimeState> UpdateAsync(
        SessionId sessionId,
        Func<SessionRuntimeState, SessionRuntimeState> update,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(sessionId, out var current))
            {
                throw new InvalidOperationException($"Runtime state '{sessionId}' is not initialized.");
            }

            var updated = update(current);
            _states[sessionId] = updated;
            return ValueTask.FromResult(updated);
        }
    }
}
