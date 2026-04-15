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
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var fallbackCandidates = PolicyRuleEvaluation.WithFallbackCandidate(candidates, context, Name);
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.TargetPriorityRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.TargetDenyRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.TargetFallbackRules, fallbackCandidates, context.Now);

        return ValueTask.FromResult(builder.Build());
    }
}
