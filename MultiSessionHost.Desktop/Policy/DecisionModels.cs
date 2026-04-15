using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Policy;

public enum DecisionDirectiveKind
{
    None = 0,
    Observe = 1,
    Navigate = 2,
    SelectSite = 3,
    SelectTarget = 4,
    PrioritizeTarget = 5,
    AvoidTarget = 6,
    UseResource = 7,
    ConserveResource = 8,
    PauseActivity = 9,
    Withdraw = 10,
    Abort = 11,
    Wait = 12
}

public enum DecisionPlanStatus
{
    Unknown = 0,
    Idle = 1,
    Ready = 2,
    Blocked = 3,
    Aborting = 4
}

public enum PolicyRuleEvaluationOutcome
{
    Considered = 0,
    Matched = 1,
    Rejected = 2,
    Skipped = 3
}

public sealed record DecisionReason(
    string SourcePolicy,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DecisionDirective(
    string DirectiveId,
    DecisionDirectiveKind DirectiveKind,
    int Priority,
    string SourcePolicy,
    string? TargetId,
    string? TargetLabel,
    string? SuggestedPolicy,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<DecisionReason> Reasons);

public sealed record PolicyEvaluationResult(
    string PolicyName,
    IReadOnlyList<DecisionDirective> Directives,
    IReadOnlyList<DecisionReason> Reasons,
    IReadOnlyList<string> Warnings,
    bool DidMatch,
    bool DidBlock,
    bool DidAbort,
    PolicyEvaluationExplanation? Explanation = null)
{
    public static PolicyEvaluationResult NoMatch(string policyName) =>
        new(policyName, [], [], [], DidMatch: false, DidBlock: false, DidAbort: false);
}

public sealed record PolicyRuleEvaluationTrace(
    string PolicyName,
    string RuleFamily,
    string RuleName,
    string RuleIntent,
    bool IsFallback,
    string CandidateId,
    string? CandidateLabel,
    PolicyRuleEvaluationOutcome Outcome,
    IReadOnlyList<string> MatchedCriteria,
    string? RejectedReason,
    IReadOnlyList<string> ProducedDirectiveKinds,
    bool Blocks,
    bool Aborts);

public sealed record PolicyEvaluationExplanation(
    string PolicyName,
    string? CandidateSummary,
    IReadOnlyList<PolicyRuleEvaluationTrace> RuleTraces,
    string? MatchedRuleName,
    bool FallbackUsed,
    IReadOnlyList<string> ProducedDirectiveKinds);

public sealed record AggregationRuleApplicationTrace(
    string RuleName,
    string RuleType,
    bool Applied,
    string? Reason,
    IReadOnlyList<string> TriggerDirectiveKinds,
    IReadOnlyList<string> SuppressedDirectiveIds,
    string? ResultStatus);

public sealed record DecisionPlanExplanation(
    IReadOnlyList<PolicyEvaluationExplanation> PolicyEvaluations,
    IReadOnlyList<AggregationRuleApplicationTrace> AggregationRulesApplied,
    IReadOnlyList<string> FinalDirectiveKinds,
    IReadOnlyList<string> FinalWarnings,
    IReadOnlyList<string> FinalReasonCodes);

public sealed record PolicyExecutionSummary(
    IReadOnlyList<string> EvaluatedPolicies,
    IReadOnlyList<string> MatchedPolicies,
    IReadOnlyList<string> BlockingPolicies,
    IReadOnlyList<string> AbortingPolicies,
    int ProducedDirectiveCount,
    int ReturnedDirectiveCount,
    IReadOnlyDictionary<string, int> SuppressedDirectiveCounts);

public sealed record DecisionPlan(
    SessionId SessionId,
    DateTimeOffset PlannedAtUtc,
    DecisionPlanStatus PlanStatus,
    IReadOnlyList<DecisionDirective> Directives,
    IReadOnlyList<DecisionReason> Reasons,
    PolicyExecutionSummary Summary,
    IReadOnlyList<string> Warnings,
    DecisionPlanExplanation? Explanation = null)
{
    public static DecisionPlan Empty(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            now,
            DecisionPlanStatus.Idle,
            [],
            [],
            new PolicyExecutionSummary([], [], [], [], 0, 0, new Dictionary<string, int>()),
            []);
}
