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

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.SiteSelectionAllowRules, candidates, context.Now, static _ => null) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.SiteSelectionFallbackRules, candidates, context.Now, static _ => null);

        return ValueTask.FromResult(builder.Build());
    }
}
