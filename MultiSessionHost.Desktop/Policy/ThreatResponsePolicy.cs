using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class ThreatResponsePolicy : IPolicy
{
    private readonly SessionHostOptions _options;
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public ThreatResponsePolicy(SessionHostOptions options)
        : this(options, new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public ThreatResponsePolicy(SessionHostOptions options, IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _options = options;
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(ThreatResponsePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidates = PolicyCandidateFactory.CreateThreatResponse(context);
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var fallbackCandidates = PolicyRuleEvaluation.WithFallbackCandidate(candidates, context, Name);
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseRetreatRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseDenyRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseFallbackRules, fallbackCandidates, context.Now);

        ApplyMemoryInfluenceIfNeeded(builder, context);

        return ValueTask.FromResult(builder.Build());
    }

    private void ApplyMemoryInfluenceIfNeeded(PolicyResultBuilder builder, PolicyEvaluationContext context)
    {
        var memoryContext = context.MemoryContext;
        var memoryOptions = _options.PolicyEngine.MemoryDecisioning;
        if (!memoryOptions.EnableMemoryDecisioning || memoryContext is null)
        {
            return;
        }

        var threatOptions = memoryOptions.ThreatResponse;
        var shouldWithdraw = MemoryInfluenceHelpers.HasRepeatedHighRiskPattern(memoryContext.RiskSummary, threatOptions);
        var currentSiteKey = context.SessionDomainState.Location.ContextLabel;
        var shouldAvoidCurrentSite = !string.IsNullOrWhiteSpace(currentSiteKey) &&
            memoryContext.KnownWorksites.Any(worksite =>
                string.Equals(worksite.WorksiteKey, currentSiteKey, StringComparison.OrdinalIgnoreCase) &&
                MemoryInfluenceHelpers.ShouldAvoidWorksiteWithRememberedRisk(worksite, threatOptions));

        if (!shouldWithdraw && !shouldAvoidCurrentSite)
        {
            return;
        }

        var reason = shouldWithdraw
            ? $"Repeated high-risk pattern detected ({memoryContext.RiskSummary.RepeatedHighRiskCount} recent observations)."
            : "Current worksite has remembered high-risk severity.";
        var reasonCode = shouldWithdraw
            ? "memory:repeated-high-risk"
            : "memory:avoid-remembered-risk";

        builder.AddReason(reasonCode, reason);
        builder.AddDirective(
            DecisionDirectiveKind.Withdraw,
            _options.PolicyEngine.ThreatResponsePolicy.WithdrawPriority,
            targetId: null,
            targetLabel: currentSiteKey,
            suggestedPolicy: "Withdraw",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["memoryInfluenced"] = "true",
                ["memoryReasonCode"] = reasonCode,
                ["memoryRepeatedHighRiskCount"] = memoryContext.RiskSummary.RepeatedHighRiskCount.ToString()
            },
            blocks: true,
            aborts: false);

        builder.AddMemoryInfluence(
            MemoryInfluenceHelpers.CreateInfluenceTrace(
                Name,
                shouldWithdraw ? "ThreatEscalation" : "ThreatAvoidance",
                currentSiteKey ?? "global",
                reasonCode,
                reason,
                shouldWithdraw
                    ? memoryContext.RiskSummary.RepeatedHighRiskCount.ToString()
                    : (currentSiteKey ?? "unknown")));
    }
}
