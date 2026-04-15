namespace MultiSessionHost.Core.Models;

public sealed record ExecutionCoordinationSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ActiveExecutionEntry> ActiveExecutions,
    IReadOnlyList<WaitingExecutionEntry> WaitingExecutions,
    IReadOnlyList<ExecutionResourceState> Resources,
    long TotalAcquisitions,
    double AverageWaitDurationMs,
    long CooldownHitCount,
    IReadOnlyList<ExecutionContentionStat> ContentionByScope);
