using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public sealed class TransitPolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public TransitPolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(TransitPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.TransitPolicy;
        var navigation = context.SessionDomainState.Navigation;

        if (navigation.Status == NavigationStatus.Blocked)
        {
            builder.AddReason("transit-blocked", "Navigation is blocked and should pause before issuing new activity directives.");
            builder.AddDirective(
                DecisionDirectiveKind.PauseActivity,
                policyOptions.BlockedPriority,
                targetId: null,
                targetLabel: navigation.DestinationLabel,
                suggestedPolicy: "PauseActivity",
                metadata: PolicyHelpers.Metadata(("destination", navigation.DestinationLabel), ("route", navigation.RouteLabel)),
                blocks: true);
        }
        else if (navigation.IsTransitioning || navigation.Status == NavigationStatus.InProgress)
        {
            builder.AddReason("transit-in-progress", "Navigation is already in progress; wait to avoid conflicting plan changes.");
            builder.AddDirective(
                DecisionDirectiveKind.Wait,
                policyOptions.WaitPriority,
                targetId: null,
                targetLabel: navigation.DestinationLabel,
                suggestedPolicy: "Wait",
                metadata: PolicyHelpers.Metadata(
                    ("destination", navigation.DestinationLabel),
                    ("route", navigation.RouteLabel),
                    ("progressPercent", navigation.ProgressPercent?.ToString("0.##"))),
                blocks: true);
        }
        else if (!string.IsNullOrWhiteSpace(navigation.DestinationLabel) && navigation.Status == NavigationStatus.Idle)
        {
            builder.AddReason("navigation-destination-known", "A destination is known and navigation is idle.");
            builder.AddDirective(
                DecisionDirectiveKind.Navigate,
                policyOptions.NavigatePriority,
                targetId: null,
                targetLabel: navigation.DestinationLabel,
                suggestedPolicy: "Navigate",
                metadata: PolicyHelpers.Metadata(("destination", navigation.DestinationLabel), ("route", navigation.RouteLabel)));
        }

        return ValueTask.FromResult(builder.Build());
    }
}
