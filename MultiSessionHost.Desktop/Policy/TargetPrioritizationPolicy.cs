using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public sealed class TargetPrioritizationPolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public TargetPrioritizationPolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(TargetPrioritizationPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.TargetPrioritizationPolicy;
        var topThreat = PolicyHelpers.GetTopThreat(context.RiskAssessmentResult);

        if (topThreat?.SuggestedPolicy == RiskPolicySuggestion.Prioritize)
        {
            builder.AddReason("risk-prioritized-target", "The top risk entity is marked for prioritization.");
            builder.AddDirective(
                DecisionDirectiveKind.PrioritizeTarget,
                policyOptions.PrioritizePriority,
                topThreat.CandidateId,
                topThreat.Name,
                topThreat.SuggestedPolicy.ToString(),
                PolicyHelpers.Metadata(("entityType", topThreat.Type), ("priority", topThreat.Priority.ToString())));
        }
        else if (topThreat?.SuggestedPolicy is RiskPolicySuggestion.Avoid or RiskPolicySuggestion.Deprioritize)
        {
            builder.AddReason("risk-avoid-target", "The top risk entity should not become the primary target.");
            builder.AddDirective(
                DecisionDirectiveKind.AvoidTarget,
                policyOptions.AvoidPriority,
                topThreat.CandidateId,
                topThreat.Name,
                topThreat.SuggestedPolicy.ToString(),
                PolicyHelpers.Metadata(("entityType", topThreat.Type), ("priority", topThreat.Priority.ToString())));
        }
        else if (context.SessionDomainState.Target.HasActiveTarget)
        {
            builder.AddReason("active-target", "The domain state has an active target.");
            builder.AddDirective(
                DecisionDirectiveKind.SelectTarget,
                policyOptions.SelectPriority,
                context.SessionDomainState.Target.PrimaryTargetId,
                context.SessionDomainState.Target.PrimaryTargetLabel,
                "SelectTarget",
                PolicyHelpers.Metadata(("targetStatus", context.SessionDomainState.Target.Status.ToString())));
        }
        else if (context.UiSemanticExtractionResult?.Targets.Count > 0)
        {
            var target = context.UiSemanticExtractionResult.Targets
                .OrderByDescending(static item => item.Active)
                .ThenByDescending(static item => item.Selected)
                .ThenByDescending(static item => item.Focused)
                .ThenBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .First();

            builder.AddReason("semantic-target-candidate", "Semantic extraction found a target candidate.");
            builder.AddDirective(
                DecisionDirectiveKind.SelectTarget,
                policyOptions.SelectPriority,
                target.NodeId,
                target.Label,
                "SelectTarget",
                PolicyHelpers.Metadata(("targetKind", target.Kind.ToString()), ("confidence", target.Confidence.ToString())));
        }

        return ValueTask.FromResult(builder.Build());
    }
}
