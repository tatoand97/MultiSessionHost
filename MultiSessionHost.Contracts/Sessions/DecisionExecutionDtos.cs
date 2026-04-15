namespace MultiSessionHost.Contracts.Sessions;

public sealed record DecisionDirectiveExecutionResultDto(
    string DirectiveId,
    string DirectiveKind,
    string PolicyName,
    int Priority,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Message,
    string? FailureCode,
    DateTimeOffset? DeferredUntilUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionPlanExecutionSummaryDto(
    int TotalDirectives,
    int SucceededCount,
    int FailedCount,
    int SkippedCount,
    int DeferredCount,
    int NotHandledCount,
    int BlockedCount,
    int AbortedCount,
    IReadOnlyList<string> ExecutedDirectiveKinds,
    IReadOnlyList<string> SkippedDirectiveKinds,
    IReadOnlyList<string> UnhandledDirectiveKinds);

public sealed record DecisionPlanExecutionDto(
    string SessionId,
    string PlanFingerprint,
    DateTimeOffset ExecutedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ExecutionStatus,
    bool WasAutoExecuted,
    IReadOnlyList<DecisionDirectiveExecutionResultDto> DirectiveResults,
    DecisionPlanExecutionSummaryDto Summary,
    DateTimeOffset? DeferredUntilUtc,
    string? FailureReason,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionPlanExecutionHistoryEntryDto(
    string SessionId,
    DateTimeOffset RecordedAtUtc,
    DecisionPlanExecutionDto Result);

public sealed record DecisionPlanExecutionHistoryDto(
    string SessionId,
    IReadOnlyList<DecisionPlanExecutionHistoryEntryDto> Entries);
