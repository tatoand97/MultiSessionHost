using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public sealed class AbortPolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public AbortPolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(AbortPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.AbortPolicy;

        if (context.SessionSnapshot.Runtime.CurrentStatus == SessionStatus.Faulted)
        {
            builder.AddReason("runtime-faulted", "The session runtime is faulted, so behavior planning must abort.");
            builder.AddDirective(
                DecisionDirectiveKind.Abort,
                policyOptions.AbortPriority,
                targetId: null,
                targetLabel: context.SessionId.Value,
                suggestedPolicy: "Abort",
                metadata: PolicyHelpers.Metadata(("currentStatus", context.SessionSnapshot.Runtime.CurrentStatus.ToString())),
                blocks: true,
                aborts: true);
        }

        if (context.SessionDomainState.Threat.Severity == ThreatSeverity.Critical &&
            string.Equals(context.SessionDomainState.Threat.TopSuggestedPolicy, RiskPolicySuggestion.Withdraw.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            builder.AddReason("critical-withdraw-threat", "The domain threat state is critical and requests withdrawal.");
            builder.AddDirective(
                DecisionDirectiveKind.Abort,
                policyOptions.AbortPriority,
                context.SessionDomainState.Target.PrimaryTargetId,
                context.SessionDomainState.Threat.TopEntityLabel,
                RiskPolicySuggestion.Withdraw.ToString(),
                PolicyHelpers.Metadata(("severity", context.SessionDomainState.Threat.Severity.ToString())),
                blocks: true,
                aborts: true);
        }

        if (context.RiskAssessmentResult?.Summary is { HighestSeverity: RiskSeverity.Critical, HasWithdrawPolicy: true } summary)
        {
            builder.AddReason("critical-risk-withdraw", "The latest risk assessment contains a critical withdrawal policy.");
            builder.AddDirective(
                DecisionDirectiveKind.Abort,
                policyOptions.AbortPriority,
                summary.TopCandidateId,
                summary.TopCandidateName,
                summary.TopSuggestedPolicy.ToString(),
                PolicyHelpers.Metadata(("topCandidateType", summary.TopCandidateType), ("highestSeverity", summary.HighestSeverity.ToString())),
                blocks: true,
                aborts: true);
        }

        if (context.SessionDomainState.Resources.IsCritical && context.SessionDomainState.Warnings.Count >= 3)
        {
            builder.AddReason("critical-resource-degraded-state", "Critical resources and multiple domain warnings make further planning unsafe.");
            builder.AddDirective(
                DecisionDirectiveKind.PauseActivity,
                policyOptions.PausePriority,
                targetId: null,
                targetLabel: context.SessionId.Value,
                suggestedPolicy: RiskPolicySuggestion.PauseActivity.ToString(),
                metadata: PolicyHelpers.Metadata(("warningCount", context.SessionDomainState.Warnings.Count.ToString())),
                blocks: true);
        }

        return ValueTask.FromResult(builder.Build());
    }
}
