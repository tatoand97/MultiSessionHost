using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class SelectNextSitePolicy : IPolicy
{
    private readonly SessionHostOptions _options;
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public SelectNextSitePolicy(SessionHostOptions options)
        : this(options, new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public SelectNextSitePolicy(SessionHostOptions options, IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _options = options;
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(SelectNextSitePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var domain = context.SessionDomainState;
        var siteLabel = domain.Location.IsUnknown
            ? _options.PolicyEngine.Rules.SiteSelection.UnknownSiteLabel
            : domain.Location.SubLocationLabel ?? domain.Location.ContextLabel ?? _options.PolicyEngine.Rules.SiteSelection.DefaultSiteLabel;
        var siteType = domain.Location.Confidence.ToString();
        var candidate = PolicyCandidateFactory.CreateSiteSelection(context, siteLabel, siteType);
        var candidates = new PolicyRuleCandidate[] { candidate };
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var rules = _ruleProvider.GetRules();

        // Apply memory-informed ranking if available
        var memoryInfluences = new List<MemoryInfluenceTrace>();
        if (_options.PolicyEngine.MemoryDecisioning.EnableMemoryDecisioning &&
            context.MemoryContext is not null &&
            context.MemoryContext.KnownWorksites.Count > 0)
        {
            ApplyMemoryInfluences(builder, context, _options.PolicyEngine.MemoryDecisioning.SiteSelection, memoryInfluences);
        }

        var result = (_ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.SiteSelectionAllowRules, candidates, context.Now, static _ => null) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.SiteSelectionFallbackRules, candidates, context.Now, static _ => null));

        // Add memory influence information to result if memory affected the decision
        if (memoryInfluences.Count > 0)
        {
            foreach (var influence in memoryInfluences)
            {
                builder.AddMemoryInfluence(influence);
            }
        }

        return ValueTask.FromResult(builder.Build());
    }

    private static void ApplyMemoryInfluences(
        PolicyResultBuilder builder,
        PolicyEvaluationContext context,
        SiteSelectionMemoryOptions memoryOptions,
        List<MemoryInfluenceTrace> influences)
    {
        if (!memoryOptions.EnableMemoryInfluence || context.MemoryContext?.KnownWorksites == null)
        {
            return;
        }

        var currentSiteKey = context.SessionDomainState.Location.ContextLabel ?? "unknown";

        // Check current worksite for penalties/boosts
        var currentSite = context.MemoryContext.KnownWorksites
            .FirstOrDefault(w => w.WorksiteKey.Equals(currentSiteKey, StringComparison.OrdinalIgnoreCase));

        if (currentSite is not null)
        {
            var influence = MemoryInfluenceHelpers.ComputeWorksiteMemoryInfluence(
                currentSite,
                memoryOptions,
                context.Now);

            if (influence > 0)
            {
                influences.Add(MemoryInfluenceHelpers.CreateInfluenceTrace(
                    "SelectNextSitePolicy",
                    "WorksiteBoost",
                    currentSiteKey,
                    "successful-worksite",
                    $"Boosting selection for worksite with {currentSite.SuccessCount} successes",
                    influence.ToString("0.##")));

                builder.AddReason(
                    "memory:successful-worksite",
                    $"Memory indicates {currentSite.SuccessCount} successful visits to {currentSiteKey}");
            }
            else if (influence < 0)
            {
                var penaltyReason = currentSite.FailureCount > 0
                    ? $"{currentSite.FailureCount} failures"
                    : currentSite.OccupancySignalCount > 0
                    ? "occupancy signals"
                    : "high remembered risk";

                influences.Add(MemoryInfluenceHelpers.CreateInfluenceTrace(
                    "SelectNextSitePolicy",
                    "WorksitePenalty",
                    currentSiteKey,
                    "memory-penalized-worksite",
                    $"Penalizing worksite due to {penaltyReason}",
                    influence.ToString("0.##")));

                builder.AddReason(
                    "memory:penalized-worksite",
                    $"Memory indicates unfavorable conditions at {currentSiteKey}: {penaltyReason}");
            }
        }

        // Check for avoided worksites with high remembered risk
        if (memoryOptions.AvoidHighRiskWorksites)
        {
            var riskySites = context.MemoryContext.KnownWorksites
                .Where(w => MemoryInfluenceHelpers.ShouldAvoidWorksiteWithRememberedRisk(
                    w,
                    new ThreatMemoryOptions
                    {
                        AvoidWorksiteWithRememberedRisk = true,
                        AvoidRiskSeverityThreshold = memoryOptions.AvoidWorksitesAboveRememberedRiskSeverity
                    }))
                .ToList();

            if (riskySites.Count > 0)
            {
                builder.AddReason(
                    "memory:avoid-high-risk",
                    $"Memory indicates {riskySites.Count} worksite(s) with high remembered risk");

                foreach (var riskySite in riskySites)
                {
                    influences.Add(MemoryInfluenceHelpers.CreateInfluenceTrace(
                        "SelectNextSitePolicy",
                        "RiskAvoidance",
                        riskySite.WorksiteKey,
                        "high-remembered-risk",
                        $"Avoiding worksite with {riskySite.LastObservedRiskSeverity} risk",
                        riskySite.WorksiteKey));
                }
            }
        }
    }
}
