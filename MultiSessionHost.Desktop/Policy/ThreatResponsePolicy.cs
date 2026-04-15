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
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var fallbackCandidates = PolicyRuleEvaluation.WithFallbackCandidate(candidates, context, Name);
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseRetreatRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseDenyRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.ThreatResponseFallbackRules, fallbackCandidates, context.Now);

        return ValueTask.FromResult(builder.Build());
    }
}
