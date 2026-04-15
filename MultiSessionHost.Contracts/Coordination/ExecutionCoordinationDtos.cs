namespace MultiSessionHost.Contracts.Coordination;

public sealed record ExecutionResourceKeyDto(
    string Scope,
    string Value);

public sealed record ExecutionResourceSetDto(
    ExecutionResourceKeyDto SessionResourceKey,
    ExecutionResourceKeyDto? TargetResourceKey,
    ExecutionResourceKeyDto? GlobalResourceKey,
    double TargetCooldownMs);

public sealed record ActiveExecutionEntryDto(
    Guid ExecutionId,
    string SessionId,
    string OperationKind,
    string? WorkItemKind,
    string? UiCommandKind,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset AcquiredAtUtc,
    double WaitDurationMs,
    double RunningDurationMs,
    ExecutionResourceSetDto ResourceSet,
    string? Description);

public sealed record WaitingExecutionEntryDto(
    Guid ExecutionId,
    string SessionId,
    string OperationKind,
    string? WorkItemKind,
    string? UiCommandKind,
    DateTimeOffset RequestedAtUtc,
    double WaitDurationMs,
    ExecutionResourceSetDto ResourceSet,
    IReadOnlyList<ExecutionResourceKeyDto> BlockingResourceKeys,
    string? Description);

public sealed record ExecutionResourceStateDto(
    ExecutionResourceKeyDto ResourceKey,
    int Capacity,
    IReadOnlyList<Guid> ActiveExecutionIds,
    IReadOnlyList<Guid> WaitingExecutionIds,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? CooldownUntilUtc);

public sealed record ExecutionContentionStatDto(
    string Scope,
    long ContentionHits);

public sealed record ExecutionCoordinationSnapshotDto(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ActiveExecutionEntryDto> ActiveExecutions,
    IReadOnlyList<WaitingExecutionEntryDto> WaitingExecutions,
    IReadOnlyList<ExecutionResourceStateDto> Resources,
    long TotalAcquisitions,
    double AverageWaitDurationMs,
    long CooldownHitCount,
    IReadOnlyList<ExecutionContentionStatDto> ContentionByScope);
