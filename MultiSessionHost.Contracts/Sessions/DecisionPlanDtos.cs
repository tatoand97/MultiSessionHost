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

public sealed record PolicyRuleEvaluationTraceDto(
    string PolicyName,
    string RuleFamily,
    string RuleName,
    string RuleIntent,
    bool IsFallback,
    string CandidateId,
    string? CandidateLabel,
    string Outcome,
    IReadOnlyList<string> MatchedCriteria,
    string? RejectedReason,
    IReadOnlyList<string> ProducedDirectiveKinds,
    bool Blocks,
    bool Aborts);

public sealed record PolicyEvaluationExplanationDto(
    string PolicyName,
    string? CandidateSummary,
    IReadOnlyList<PolicyRuleEvaluationTraceDto> RuleTraces,
    string? MatchedRuleName,
    bool FallbackUsed,
    IReadOnlyList<string> ProducedDirectiveKinds);

public sealed record AggregationRuleApplicationTraceDto(
    string RuleName,
    string RuleType,
    bool Applied,
    string? Reason,
    IReadOnlyList<string> TriggerDirectiveKinds,
    IReadOnlyList<string> SuppressedDirectiveIds,
    string? ResultStatus);

public sealed record DecisionPlanExplanationDto(
    string SessionId,
    DateTimeOffset PlannedAtUtc,
    PolicyRuleSetDto EffectiveRules,
    IReadOnlyList<PolicyEvaluationExplanationDto> PolicyEvaluations,
    IReadOnlyList<AggregationRuleApplicationTraceDto> AggregationRulesApplied,
    IReadOnlyList<DecisionDirectiveDto> FinalDirectives,
    IReadOnlyList<DecisionReasonDto> FinalReasons,
    IReadOnlyList<string> FinalWarnings,
    IReadOnlyList<string> FinalReasonCodes);
