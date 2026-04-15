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

        foreach (var rule in _ruleProvider.GetRules().ResourceUsageRules)
        {
            if (!_matcher.IsMatch(rule, candidate, out var matchedCriteria))
            {
                continue;
            }

            builder.AddReason(rule.RuleName, rule.Reason);
            builder.AddDirective(
                rule.DirectiveKind,
                rule.Priority,
                targetId: null,
                PolicyHelpers.ResolveTargetLabel(rule, candidate),
                rule.SuggestedPolicy,
                PolicyHelpers.RuleMetadata(rule, candidate, matchedCriteria, context.Now),
                rule.Blocks,
                rule.Aborts);
            break;
        }

        return ValueTask.FromResult(builder.Build());
    }
}
