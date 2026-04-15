using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.PolicyControl;

namespace MultiSessionHost.Desktop.Policy;

public interface IPolicyEngine
{
    ValueTask<DecisionPlan> EvaluateAsync(SessionId sessionId, CancellationToken cancellationToken);
}

public interface IPolicyControlGate
{
    ValueTask<PolicyEvaluationGateResult> GetEvaluationGateAsync(SessionId sessionId, CancellationToken cancellationToken);
}

public interface IPolicy
{
    string Name { get; }

    ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken);
}

public interface IDecisionPlanAggregator
{
    DecisionPlan Aggregate(
        SessionId sessionId,
        DateTimeOffset plannedAtUtc,
        IReadOnlyList<PolicyEvaluationResult> policyResults);
}

public interface ISessionDecisionPlanStore
{
    ValueTask InitializeAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken);

    ValueTask<DecisionPlan?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<DecisionPlan>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<DecisionPlanHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<DecisionPlan> UpdateAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken);

    ValueTask RestoreAsync(
        SessionId sessionId,
        DecisionPlan? latestPlan,
        IReadOnlyList<DecisionPlanHistoryEntry> history,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
