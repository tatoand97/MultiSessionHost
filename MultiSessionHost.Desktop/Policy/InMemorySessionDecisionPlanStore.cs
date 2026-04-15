using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Policy;

public sealed class InMemorySessionDecisionPlanStore : ISessionDecisionPlanStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, DecisionPlan> _plans = [];

    public ValueTask InitializeAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_plans.ContainsKey(sessionId))
            {
                throw new InvalidOperationException($"Decision plan for session '{sessionId}' is already initialized.");
            }

            _plans[sessionId] = plan;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<DecisionPlan?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_plans.TryGetValue(sessionId, out var plan) ? plan : null);
        }
    }

    public ValueTask<IReadOnlyCollection<DecisionPlan>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<DecisionPlan>>(_plans.Values.ToArray());
        }
    }

    public ValueTask<DecisionPlan> UpdateAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _plans[sessionId] = plan;
            return ValueTask.FromResult(plan);
        }
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _plans.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }
}
