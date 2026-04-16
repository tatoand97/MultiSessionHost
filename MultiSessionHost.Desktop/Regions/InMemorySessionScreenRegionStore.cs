using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Regions;

public sealed class InMemorySessionScreenRegionStore : ISessionScreenRegionStore
{
    private sealed class SessionRegionState
    {
        public SessionScreenRegionResolution? Latest { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionRegionState> _stateBySessionId = new();

    public ValueTask<SessionScreenRegionResolution> UpsertLatestAsync(SessionId sessionId, SessionScreenRegionResolution resolution, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stateBySessionId.TryGetValue(sessionId, out var state))
            {
                state = new SessionRegionState();
                _stateBySessionId[sessionId] = state;
            }

            state.Latest = resolution;
        }

        return ValueTask.FromResult(resolution);
    }

    public ValueTask<SessionScreenRegionResolution?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionScreenRegionResolution>> GetAllLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionScreenRegionResolution>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest)
                    .Where(static resolution => resolution is not null)
                    .Select(static resolution => resolution!)
                    .ToArray());
        }
    }

    public ValueTask<SessionScreenRegionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest?.ToSummary() : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionScreenRegionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionScreenRegionSummary>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest?.ToSummary())
                    .Where(static summary => summary is not null)
                    .Select(static summary => summary!)
                    .ToArray());
        }
    }
}