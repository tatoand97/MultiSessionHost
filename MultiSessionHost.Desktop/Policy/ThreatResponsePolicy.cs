using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class ThreatResponsePolicy : IPolicy
{
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public ThreatResponsePolicy(SessionHostOptions options)
        : this(new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public ThreatResponsePolicy(IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(ThreatResponsePolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidates = PolicyCandidateFactory.CreateThreatResponse(context);

        foreach (var rule in _ruleProvider.GetRules().ThreatResponseRules)
        {
            var candidate = candidates.FirstOrDefault(candidate => _matcher.IsMatch(rule, candidate, out _));

            if (candidate is null || !_matcher.IsMatch(rule, candidate, out var matchedCriteria))
            {
                continue;
            }

            builder.AddReason(rule.RuleName, rule.Reason);
            builder.AddDirective(
                rule.DirectiveKind,
                rule.Priority,
                candidate.CandidateId,
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
