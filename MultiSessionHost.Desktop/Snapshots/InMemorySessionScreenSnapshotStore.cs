using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class InMemorySessionScreenSnapshotStore : ISessionScreenSnapshotStore
{
    private sealed class SessionSnapshotState
    {
        public SessionScreenSnapshot? Latest { get; set; }

        public List<SessionScreenSnapshotSummary> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly int _maxHistoryEntries;
    private readonly Dictionary<SessionId, SessionSnapshotState> _states = [];

    public InMemorySessionScreenSnapshotStore(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxHistoryEntries = options.ScreenSnapshots.MaxHistoryEntriesPerSession;
    }

    public ValueTask<SessionScreenSnapshot> UpsertLatestAsync(SessionId sessionId, SessionScreenSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Latest = snapshot;
            state.History.Add(snapshot.ToSummary());

            if (state.History.Count > _maxHistoryEntries)
            {
                state.History.RemoveRange(0, state.History.Count - _maxHistoryEntries);
            }

            return ValueTask.FromResult(snapshot);
        }
    }

    public ValueTask<SessionScreenSnapshot?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state.Latest : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionScreenSnapshot>> GetAllLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionScreenSnapshot>>(
                _states.Values
                    .Where(static state => state.Latest is not null)
                    .Select(static state => state.Latest!)
                    .OrderBy(static snapshot => snapshot.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask<SessionScreenSnapshotSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state.Latest?.ToSummary() : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionScreenSnapshotSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionScreenSnapshotSummary>>(
                _states.Values
                    .Where(static state => state.Latest is not null)
                    .Select(static state => state.Latest!.ToSummary())
                    .OrderBy(static summary => summary.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask<SessionScreenSnapshotHistory> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(
                new SessionScreenSnapshotHistory(
                    sessionId,
                    _states.TryGetValue(sessionId, out var state)
                        ? state.History.ToArray()
                        : []));
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

    private SessionSnapshotState GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionSnapshotState();
        _states[sessionId] = state;
        return state;
    }
}
