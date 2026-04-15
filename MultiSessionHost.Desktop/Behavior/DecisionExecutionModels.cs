using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Behavior;

public enum DecisionPlanExecutionStatus
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2,
    Deferred = 3,
    Blocked = 4,
    Aborted = 5,
    Skipped = 6,
    NoOp = 7
}

public enum DecisionDirectiveExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    Skipped = 2,
    Deferred = 3,
    NotHandled = 4,
    Blocked = 5,
    Aborted = 6
}

public sealed record DecisionPlanExecutionContext(
    SessionId SessionId,
    DecisionPlan DecisionPlan,
    SessionDomainState? DomainState,
    RiskAssessmentResult? RiskAssessment,
    SessionActivitySnapshot? ActivitySnapshot,
    DateTimeOffset RequestedAtUtc,
    bool WasAutoExecuted);

public sealed record DecisionDirectiveExecutionContext(
    SessionId SessionId,
    DecisionPlan DecisionPlan,
    SessionDomainState? DomainState,
    RiskAssessmentResult? RiskAssessment,
    SessionActivitySnapshot? ActivitySnapshot,
    DateTimeOffset ExecutionStartedAtUtc,
    bool WasAutoExecuted);

public sealed record DecisionDirectiveExecutionResult(
    string DirectiveId,
    DecisionDirectiveKind DirectiveKind,
    string PolicyName,
    int Priority,
    DecisionDirectiveExecutionStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Message,
    string? FailureCode,
    DateTimeOffset? DeferredUntilUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionPlanExecutionSummary(
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

public sealed record DecisionPlanExecutionResult(
    SessionId SessionId,
    string PlanFingerprint,
    DateTimeOffset ExecutedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DecisionPlanExecutionStatus ExecutionStatus,
    bool WasAutoExecuted,
    IReadOnlyList<DecisionDirectiveExecutionResult> DirectiveResults,
    DecisionPlanExecutionSummary Summary,
    DateTimeOffset? DeferredUntilUtc,
    string? FailureReason,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionPlanExecutionRecord(
    SessionId SessionId,
    DateTimeOffset RecordedAtUtc,
    DecisionPlanExecutionResult Result);
