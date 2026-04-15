using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public sealed class ThreatResponsePolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public ThreatResponsePolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(ThreatResponsePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.ThreatResponsePolicy;
        var threat = context.SessionDomainState.Threat;
        var topThreat = PolicyHelpers.GetTopThreat(context.RiskAssessmentResult);
        var suggestedPolicy = topThreat?.SuggestedPolicy.ToString() ?? threat.TopSuggestedPolicy;
        var targetId = topThreat?.CandidateId ?? context.SessionDomainState.Target.PrimaryTargetId;
        var targetLabel = topThreat?.Name ?? threat.TopEntityLabel ?? context.SessionDomainState.Target.PrimaryTargetLabel;

        if (string.Equals(suggestedPolicy, RiskPolicySuggestion.Withdraw.ToString(), StringComparison.OrdinalIgnoreCase) ||
            threat.Severity == ThreatSeverity.Critical)
        {
            builder.AddReason("withdraw-threat", "A severe threat requests withdrawal before normal planning continues.");
            builder.AddDirective(
                DecisionDirectiveKind.Withdraw,
                policyOptions.WithdrawPriority,
                targetId,
                targetLabel,
                RiskPolicySuggestion.Withdraw.ToString(),
                PolicyHelpers.Metadata(("severity", threat.Severity.ToString()), ("suggestedPolicy", suggestedPolicy)),
                blocks: true);
        }
        else if (string.Equals(suggestedPolicy, RiskPolicySuggestion.PauseActivity.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            builder.AddReason("pause-threat", "The top threat requests pausing activity.");
            builder.AddDirective(
                DecisionDirectiveKind.PauseActivity,
                policyOptions.PausePriority,
                targetId,
                targetLabel,
                RiskPolicySuggestion.PauseActivity.ToString(),
                PolicyHelpers.Metadata(("severity", threat.Severity.ToString())),
                blocks: true);
        }
        else if (string.Equals(suggestedPolicy, RiskPolicySuggestion.Prioritize.ToString(), StringComparison.OrdinalIgnoreCase) && topThreat is not null)
        {
            builder.AddReason("prioritized-threat", "A classified threat is the highest priority entity.");
            builder.AddDirective(
                DecisionDirectiveKind.PrioritizeTarget,
                policyOptions.PrioritizePriority,
                topThreat.CandidateId,
                topThreat.Name,
                topThreat.SuggestedPolicy.ToString(),
                PolicyHelpers.Metadata(("severity", topThreat.Severity.ToString()), ("entityType", topThreat.Type)));
        }
        else if (string.Equals(suggestedPolicy, RiskPolicySuggestion.Avoid.ToString(), StringComparison.OrdinalIgnoreCase) && topThreat is not null)
        {
            builder.AddReason("avoid-threat", "A classified threat should be avoided.");
            builder.AddDirective(
                DecisionDirectiveKind.AvoidTarget,
                policyOptions.AvoidPriority,
                topThreat.CandidateId,
                topThreat.Name,
                topThreat.SuggestedPolicy.ToString(),
                PolicyHelpers.Metadata(("severity", topThreat.Severity.ToString()), ("entityType", topThreat.Type)));
        }
        else if (threat.Severity == ThreatSeverity.Unknown || context.RiskAssessmentResult?.Summary.UnknownCount > 0)
        {
            builder.AddReason("observe-unknown-threat", "Threat posture is unknown, so the next behavior should observe before committing.");
            builder.AddDirective(
                DecisionDirectiveKind.Observe,
                policyOptions.ObservePriority,
                targetId,
                targetLabel,
                RiskPolicySuggestion.Observe.ToString(),
                PolicyHelpers.Metadata(("severity", threat.Severity.ToString())));
        }

        return ValueTask.FromResult(builder.Build());
    }
}
