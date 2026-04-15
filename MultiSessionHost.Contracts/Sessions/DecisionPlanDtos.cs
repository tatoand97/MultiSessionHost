namespace MultiSessionHost.Contracts.Sessions;

public sealed record DecisionReasonDto(
    string SourcePolicy,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionDirectiveDto(
    string DirectiveId,
    string DirectiveKind,
    int Priority,
    string SourcePolicy,
    string? TargetId,
    string? TargetLabel,
    string? SuggestedPolicy,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<DecisionReasonDto> Reasons);

public sealed record PolicyExecutionSummaryDto(
    IReadOnlyList<string> EvaluatedPolicies,
    IReadOnlyList<string> MatchedPolicies,
    IReadOnlyList<string> BlockingPolicies,
    IReadOnlyList<string> AbortingPolicies,
    int ProducedDirectiveCount,
    int ReturnedDirectiveCount,
    IReadOnlyDictionary<string, int> SuppressedDirectiveCounts);

public sealed record DecisionPlanDto(
    string SessionId,
    DateTimeOffset PlannedAtUtc,
    string PlanStatus,
    IReadOnlyList<DecisionDirectiveDto> Directives,
    IReadOnlyList<DecisionReasonDto> Reasons,
    PolicyExecutionSummaryDto Summary,
    IReadOnlyList<string> Warnings);

public sealed record DecisionPlanSummaryDto(
    string SessionId,
    DateTimeOffset PlannedAtUtc,
    string PlanStatus,
    int DirectiveCount,
    IReadOnlyList<string> EvaluatedPolicies,
    IReadOnlyList<string> MatchedPolicies,
    IReadOnlyList<string> BlockingPolicies,
    IReadOnlyList<string> AbortingPolicies,
    IReadOnlyList<string> Warnings);
