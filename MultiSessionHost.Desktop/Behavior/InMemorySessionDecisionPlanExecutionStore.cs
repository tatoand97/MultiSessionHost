using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class InMemorySessionDecisionPlanExecutionStore : ISessionDecisionPlanExecutionStore
{
    private sealed class SessionExecutionState
    {
        public DecisionPlanExecutionResult? Current { get; set; }

        public List<DecisionPlanExecutionRecord> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly int _maxHistoryEntries;
    private readonly Dictionary<SessionId, SessionExecutionState> _states = [];

    public InMemorySessionDecisionPlanExecutionStore(SessionHostOptions options)
    {
        _maxHistoryEntries = options.DecisionExecution.MaxHistoryEntries;
    }

    public DecisionPlanExecutionResult? GetCurrent(SessionId sessionId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(sessionId, out var state) ? state.Current : null;
        }
    }

    public IReadOnlyCollection<DecisionPlanExecutionResult> GetAllCurrent()
    {
        lock (_gate)
        {
            return _states.Values
                .Where(static state => state.Current is not null)
                .Select(static state => state.Current!)
                .ToArray();
        }
    }

    public IReadOnlyList<DecisionPlanExecutionRecord> GetHistory(SessionId sessionId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(sessionId, out var state)
                ? state.History.ToArray()
                : [];
        }
    }

    public ValueTask<DecisionPlanExecutionResult?> GetCurrentAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetCurrent(sessionId));

    public ValueTask<IReadOnlyCollection<DecisionPlanExecutionResult>> GetAllCurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetAllCurrent());

    public ValueTask<IReadOnlyList<DecisionPlanExecutionRecord>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetHistory(sessionId));

    public ValueTask InitializeIfMissingAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_states.ContainsKey(sessionId))
            {
                _states[sessionId] = new SessionExecutionState();
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpsertCurrentAsync(SessionId sessionId, DecisionPlanExecutionResult executionResult, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current = executionResult;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AppendHistoryAsync(SessionId sessionId, DecisionPlanExecutionRecord record, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.History.Add(record);

            if (state.History.Count > _maxHistoryEntries)
            {
                var removeCount = state.History.Count - _maxHistoryEntries;
                state.History.RemoveRange(0, removeCount);
            }
        }

        return ValueTask.CompletedTask;
    }

    private SessionExecutionState GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionExecutionState();
        _states[sessionId] = state;
        return state;
    }
}
