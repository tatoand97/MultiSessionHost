namespace MultiSessionHost.Core.Models;

public sealed record ExecutionResourceState(
    ExecutionResourceKey ResourceKey,
    int Capacity,
    IReadOnlyList<Guid> ActiveExecutionIds,
    IReadOnlyList<Guid> WaitingExecutionIds,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? CooldownUntilUtc);
