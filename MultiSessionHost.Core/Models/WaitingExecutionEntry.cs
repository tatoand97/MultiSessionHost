namespace MultiSessionHost.Core.Models;

public sealed record WaitingExecutionEntry(
    ExecutionRequest Request,
    TimeSpan WaitDuration,
    IReadOnlyList<ExecutionResourceKey> BlockingResourceKeys);
