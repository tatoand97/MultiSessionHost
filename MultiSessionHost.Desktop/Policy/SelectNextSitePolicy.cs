using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public sealed class SelectNextSitePolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public SelectNextSitePolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(SelectNextSitePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.SelectNextSitePolicy;
        var domain = context.SessionDomainState;
        var safeEnough = domain.Threat.IsSafe == true ||
            domain.Threat.Severity is ThreatSeverity.None or ThreatSeverity.Low or ThreatSeverity.Unknown;

        if (!domain.Navigation.IsTransitioning &&
            domain.Navigation.Status is NavigationStatus.Idle or NavigationStatus.Unknown &&
            domain.Combat.Status is CombatStatus.Idle or CombatStatus.Unknown &&
            !domain.Target.HasActiveTarget &&
            safeEnough)
        {
            var siteLabel = domain.Location.IsUnknown
                ? "unknown-worksite"
                : domain.Location.SubLocationLabel ?? domain.Location.ContextLabel ?? "worksite";

            builder.AddReason("idle-safe-site-selection", "The session is idle enough to choose the next work context.");
            builder.AddDirective(
                DecisionDirectiveKind.SelectSite,
                policyOptions.SelectSitePriority,
                targetId: null,
                targetLabel: siteLabel,
                suggestedPolicy: "SelectSite",
                metadata: PolicyHelpers.Metadata(
                    ("locationConfidence", domain.Location.Confidence.ToString()),
                    ("contextLabel", domain.Location.ContextLabel)));
        }
        else if (!PolicyHelpers.IsSevere(domain.Threat.Severity))
        {
            builder.AddReason("observe-before-site-selection", "The session is not ready for site selection; observe current state.");
            builder.AddDirective(
                DecisionDirectiveKind.Observe,
                policyOptions.ObservePriority,
                targetId: null,
                targetLabel: domain.Location.ContextLabel,
                suggestedPolicy: "Observe",
                metadata: PolicyHelpers.Metadata(
                    ("navigationStatus", domain.Navigation.Status.ToString()),
                    ("combatStatus", domain.Combat.Status.ToString()),
                    ("hasActiveTarget", domain.Target.HasActiveTarget.ToString())));
        }

        return ValueTask.FromResult(builder.Build());
    }
}
