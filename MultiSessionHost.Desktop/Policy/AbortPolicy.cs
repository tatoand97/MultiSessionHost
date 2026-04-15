using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class AbortPolicy : IPolicy
{
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public AbortPolicy(SessionHostOptions options)
        : this(new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public AbortPolicy(IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(AbortPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidate = PolicyCandidateFactory.CreateAbort(context);
        var candidates = new PolicyRuleCandidate[] { candidate };
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.AbortRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.AbortFallbackRules, candidates, context.Now);

        return ValueTask.FromResult(builder.Build());
    }
}
