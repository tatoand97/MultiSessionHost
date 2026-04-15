using MultiSessionHost.Core.Configuration;

namespace MultiSessionHost.Desktop.Policy;

public sealed class ResourceUsagePolicy : IPolicy
{
    private readonly SessionHostOptions _options;

    public ResourceUsagePolicy(SessionHostOptions options)
    {
        _options = options;
    }

    public string Name => nameof(ResourceUsagePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var policyOptions = _options.PolicyEngine.ResourceUsagePolicy;
        var resources = context.SessionDomainState.Resources;
        var lowestPercent = new[] { resources.HealthPercent, resources.CapacityPercent, resources.EnergyPercent }
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty(100)
            .Min();

        if (resources.IsCritical || lowestPercent <= policyOptions.CriticalPercentThreshold || resources.AvailableChargeCount == 0)
        {
            builder.AddReason("critical-resource", "One or more resources are critical.");
            builder.AddDirective(
                DecisionDirectiveKind.Withdraw,
                policyOptions.CriticalPriority,
                targetId: null,
                targetLabel: "resources",
                suggestedPolicy: "Withdraw",
                metadata: PolicyHelpers.Metadata(
                    ("lowestPercent", lowestPercent.ToString("0.##")),
                    ("availableChargeCount", resources.AvailableChargeCount?.ToString())),
                blocks: true);
        }
        else if (resources.IsDegraded || lowestPercent <= policyOptions.DegradedPercentThreshold)
        {
            builder.AddReason("degraded-resource", "Resource posture is degraded and should be conserved.");
            builder.AddDirective(
                DecisionDirectiveKind.ConserveResource,
                policyOptions.DegradedPriority,
                targetId: null,
                targetLabel: "resources",
                suggestedPolicy: "ConserveResource",
                metadata: PolicyHelpers.Metadata(("lowestPercent", lowestPercent.ToString("0.##"))));
        }
        else if (context.SessionDomainState.Combat.DefensivePostureActive)
        {
            builder.AddReason("defensive-resource-use", "Defensive posture is active and resources are available.");
            builder.AddDirective(
                DecisionDirectiveKind.UseResource,
                policyOptions.DegradedPriority,
                targetId: null,
                targetLabel: "defensive-posture",
                suggestedPolicy: "UseResource",
                metadata: PolicyHelpers.Metadata(("activityPhase", context.SessionDomainState.Combat.ActivityPhase)));
        }

        return ValueTask.FromResult(builder.Build());
    }
}
