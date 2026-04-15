using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Policy;

public sealed class InMemorySessionDecisionPlanStore : ISessionDecisionPlanStore
{
    private sealed class SessionDecisionPlanState
    {
        public DecisionPlan? Current { get; set; }

        public List<DecisionPlanHistoryEntry> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly int _maxHistoryEntries;
    private readonly Dictionary<SessionId, SessionDecisionPlanState> _states = [];

    public InMemorySessionDecisionPlanStore(SessionHostOptions options)
    {
        _maxHistoryEntries = options.RuntimePersistence.MaxDecisionHistoryEntries;
    }

    public ValueTask InitializeAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(sessionId, out var existing) && existing.Current is not null)
            {
                throw new InvalidOperationException($"Decision plan for session '{sessionId}' is already initialized.");
            }

            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current = plan;
            AppendHistoryUnsafe(state, new DecisionPlanHistoryEntry(sessionId, plan.PlannedAtUtc, plan));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<DecisionPlan?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_states.TryGetValue(sessionId, out var state) ? state.Current : null);
        }
    }

    public ValueTask<IReadOnlyCollection<DecisionPlan>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<DecisionPlan>>(
                _states.Values
                    .Where(static state => state.Current is not null)
                    .Select(static state => state.Current!)
                    .ToArray());
        }
    }

    public ValueTask<IReadOnlyList<DecisionPlanHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<DecisionPlanHistoryEntry>>(
                _states.TryGetValue(sessionId, out var state) ? state.History.ToArray() : []);
        }
    }

    public ValueTask<DecisionPlan> UpdateAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current = plan;
            AppendHistoryUnsafe(state, new DecisionPlanHistoryEntry(sessionId, plan.PlannedAtUtc, plan));
            return ValueTask.FromResult(plan);
        }
    }

    public ValueTask RestoreAsync(
        SessionId sessionId,
        DecisionPlan? latestPlan,
        IReadOnlyList<DecisionPlanHistoryEntry> history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        lock (_gate)
        {
            var state = GetOrCreateStateUnsafe(sessionId);
            state.Current = latestPlan;
            state.History.Clear();
            state.History.AddRange(history
                .Where(entry => entry.SessionId == sessionId)
                .OrderBy(static entry => entry.RecordedAtUtc)
                .TakeLast(_maxHistoryEntries));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _states.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }

    private SessionDecisionPlanState GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionDecisionPlanState();
        _states[sessionId] = state;
        return state;
    }

    private void AppendHistoryUnsafe(SessionDecisionPlanState state, DecisionPlanHistoryEntry entry)
    {
        state.History.Add(entry);

        if (state.History.Count > _maxHistoryEntries)
        {
            state.History.RemoveRange(0, state.History.Count - _maxHistoryEntries);
        }
    }
}
