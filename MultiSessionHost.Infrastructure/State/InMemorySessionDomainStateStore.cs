using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.State;

public sealed class InMemorySessionDomainStateStore : ISessionDomainStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionDomainState> _states = [];

    public ValueTask InitializeAsync(SessionDomainState state, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_states.ContainsKey(state.SessionId))
            {
                throw new InvalidOperationException($"Session domain state '{state.SessionId}' is already initialized.");
            }

            _states[state.SessionId] = state;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SessionDomainState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionDomainState>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionDomainState>>(_states.Values.ToArray());
        }
    }

    public ValueTask<SessionDomainState> UpdateAsync(
        SessionId sessionId,
        Func<SessionDomainState, SessionDomainState> update,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(sessionId, out var current))
            {
                throw new InvalidOperationException($"Session domain state '{sessionId}' is not initialized.");
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
