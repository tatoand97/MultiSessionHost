using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class TargetPrioritizationPolicy : IPolicy
{
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public TargetPrioritizationPolicy(SessionHostOptions options)
        : this(new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public TargetPrioritizationPolicy(IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(TargetPrioritizationPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidates = PolicyCandidateFactory.CreateTargetPriority(context);

        foreach (var rule in _ruleProvider.GetRules().TargetPriorityRules)
        {
            var matchedCandidate = candidates.FirstOrDefault(candidate => _matcher.IsMatch(rule, candidate, out _));

            if (matchedCandidate is null || !_matcher.IsMatch(rule, matchedCandidate, out var matchedCriteria))
            {
                continue;
            }

            builder.AddReason(rule.RuleName, rule.Reason);
            builder.AddDirective(
                rule.DirectiveKind,
                rule.Priority,
                matchedCandidate.CandidateId,
                PolicyHelpers.ResolveTargetLabel(rule, matchedCandidate),
                rule.SuggestedPolicy,
                PolicyHelpers.RuleMetadata(rule, matchedCandidate, matchedCriteria, context.Now),
                rule.Blocks,
                rule.Aborts);
            break;
        }

        return ValueTask.FromResult(builder.Build());
    }
}
