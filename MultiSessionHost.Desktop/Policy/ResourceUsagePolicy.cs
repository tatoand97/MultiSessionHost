using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class ResourceUsagePolicy : IPolicy
{
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public ResourceUsagePolicy(SessionHostOptions options)
        : this(new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public ResourceUsagePolicy(IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(ResourceUsagePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidate = PolicyCandidateFactory.CreateResourceUsage(context);
        var candidates = new PolicyRuleCandidate[] { candidate };
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ResourceUsageRules, candidates, context.Now, static _ => null) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ResourceUsageFallbackRules, candidates, context.Now, static _ => null);

        return ValueTask.FromResult(builder.Build());
    }
}
