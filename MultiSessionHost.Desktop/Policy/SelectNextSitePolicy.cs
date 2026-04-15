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

        foreach (var rule in _ruleProvider.GetRules().SiteSelectionRules)
        {
            if (!_matcher.IsMatch(rule, candidate, out var matchedCriteria))
            {
                builder.AddReason(rule.RuleName + "-rejected", "Site selection candidate did not match configured rule.");
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
            return ValueTask.FromResult(builder.Build());
        }

        if (!_options.PolicyEngine.Rules.SiteSelection.IgnoreNonAllowlistedSites)
        {
            var directiveKind = Enum.Parse<DecisionDirectiveKind>(_options.PolicyEngine.Rules.SiteSelection.NoAllowedCandidateDirectiveKind, ignoreCase: true);
            builder.AddReason("no-allowed-site", "No configured site-selection rule accepted the candidate.");
            builder.AddDirective(
                directiveKind,
                _options.PolicyEngine.Rules.SiteSelection.NoAllowedCandidatePriority,
                targetId: null,
                siteLabel,
                directiveKind.ToString(),
                PolicyHelpers.Metadata(("siteLabel", siteLabel)),
                blocks: directiveKind is DecisionDirectiveKind.Wait or DecisionDirectiveKind.PauseActivity);
        }

        return ValueTask.FromResult(builder.Build());
    }
}
