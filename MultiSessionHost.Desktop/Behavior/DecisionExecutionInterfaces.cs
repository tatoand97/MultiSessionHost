using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.Behavior;

public interface ISessionDecisionPlanExecutionStore
{
    DecisionPlanExecutionResult? GetCurrent(SessionId sessionId);

    IReadOnlyCollection<DecisionPlanExecutionResult> GetAllCurrent();

    IReadOnlyList<DecisionPlanExecutionRecord> GetHistory(SessionId sessionId);

    ValueTask<DecisionPlanExecutionResult?> GetCurrentAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<DecisionPlanExecutionResult>> GetAllCurrentAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<DecisionPlanExecutionRecord>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask InitializeIfMissingAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask UpsertCurrentAsync(SessionId sessionId, DecisionPlanExecutionResult executionResult, CancellationToken cancellationToken);

    ValueTask AppendHistoryAsync(SessionId sessionId, DecisionPlanExecutionRecord record, CancellationToken cancellationToken);
}

public interface IDecisionPlanExecutor
{
    ValueTask<DecisionPlanExecutionResult> ExecuteLatestAsync(SessionId sessionId, bool wasAutoExecuted, CancellationToken cancellationToken);

    ValueTask<DecisionPlanExecutionResult> ExecuteAsync(DecisionPlanExecutionContext context, CancellationToken cancellationToken);
}

public interface IDecisionDirectiveHandler
{
    bool CanHandle(DecisionDirective directive);

    ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken);
}

public interface ISessionControlGateway
{
    ValueTask PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask AbortSessionAsync(SessionId sessionId, CancellationToken cancellationToken);
}
