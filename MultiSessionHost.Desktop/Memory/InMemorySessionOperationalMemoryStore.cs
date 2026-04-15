using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Memory;

public sealed class InMemorySessionOperationalMemoryStore : ISessionOperationalMemoryStore
{
    private sealed class SessionMemoryState
    {
        public SessionOperationalMemorySnapshot? Current { get; set; }

        public List<MemoryObservationRecord> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly int _maxHistoryEntries;
    private readonly Dictionary<SessionId, SessionMemoryState> _states = [];

    public InMemorySessionOperationalMemoryStore(SessionHostOptions options)
    {
        _maxHistoryEntries = options.OperationalMemory.MaxHistoryEntries;
    }

    public SessionOperationalMemorySnapshot? GetCurrent(SessionId sessionId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(sessionId, out var state) ? state.Current : null;
        }
    }

    public IReadOnlyCollection<SessionOperationalMemorySnapshot> GetAllCurrent()
    {
        lock (_gate)
        {
            return _states.Values
                .Where(static state => state.Current is not null)
                .Select(static state => state.Current!)
                .ToArray();
        }
    }

    public ValueTask<SessionOperationalMemorySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetCurrent(sessionId));

    public ValueTask<IReadOnlyCollection<SessionOperationalMemorySnapshot>> GetAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetAllCurrent());

    public ValueTask<IReadOnlyList<MemoryObservationRecord>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<MemoryObservationRecord>>(
                _states.TryGetValue(sessionId, out var state) ? state.History.ToArray() : []);
        }
    }

    public ValueTask<IReadOnlyList<WorksiteObservation>> GetKnownWorksitesAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<WorksiteObservation>>(
                _states.TryGetValue(sessionId, out var state) && state.Current is not null
                    ? state.Current.KnownWorksites.ToArray()
                    : []);
        }
    }

    public ValueTask<SessionOperationalMemorySummary?> GetSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(
                _states.TryGetValue(sessionId, out var state) ? state.Current?.Summary : null);
        }
    }

    public ValueTask<WorksiteObservation?> GetLatestWorksiteObservationAsync(
        SessionId sessionId,
        string worksiteKey,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var observation = _states.TryGetValue(sessionId, out var state) && state.Current is not null
                ? state.Current.KnownWorksites.FirstOrDefault(item => string.Equals(item.WorksiteKey, worksiteKey, StringComparison.OrdinalIgnoreCase))
                : null;

            return ValueTask.FromResult(observation);
        }
    }

    public ValueTask InitializeIfMissingAsync(SessionId sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current ??= SessionOperationalMemorySnapshot.Empty(sessionId, now);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpsertAsync(
        SessionId sessionId,
        SessionOperationalMemorySnapshot snapshot,
        IReadOnlyList<MemoryObservationRecord> newObservationRecords,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(newObservationRecords);

        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current = snapshot;
            state.History.AddRange(newObservationRecords);

            if (state.History.Count > _maxHistoryEntries)
            {
                state.History.RemoveRange(0, state.History.Count - _maxHistoryEntries);
            }
        }

        return ValueTask.CompletedTask;
    }

    private SessionMemoryState GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionMemoryState();
        _states[sessionId] = state;
        return state;
    }
}
