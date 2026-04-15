using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public interface IPolicyRuleProvider
{
    PolicyRuleSet GetRules();
}

public sealed class ConfiguredPolicyRuleProvider : IPolicyRuleProvider
{
    private readonly PolicyRuleSet _rules;

    public ConfiguredPolicyRuleProvider(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rules = Build(options.PolicyEngine);
    }

    public PolicyRuleSet GetRules() => _rules;

    private static PolicyRuleSet Build(PolicyEngineOptions options)
    {
        var siteAllowRules = Merge(options.Rules.SiteSelection.AllowRules, options.SelectNextSitePolicy.Rules.AllowRules)
            .Select(rule => CreateSiteSelectionRule(rule, nameof(SelectNextSitePolicy), "SiteSelection.AllowRules", "Allow", "PolicyEngine.Rules.SiteSelection.AllowRules"))
            .ToArray();
        var siteFallbackRules = BuildSiteSelectionFallback(options)
            .Select(rule => CreateSiteSelectionRule(rule, nameof(SelectNextSitePolicy), "SiteSelection.Fallback", "Fallback", "PolicyEngine.Rules.SiteSelection.Fallback", isFallback: true))
            .ToArray();
        var threatRetreatRules = Merge(options.Rules.ThreatResponse.RetreatRules, options.ThreatResponsePolicy.Rules.RetreatRules)
            .Select(rule => CreateThreatResponseRule(rule, nameof(ThreatResponsePolicy), "ThreatResponse.RetreatRules", "Retreat", "PolicyEngine.Rules.ThreatResponse.RetreatRules"))
            .ToArray();
        var threatDenyRules = Merge(options.Rules.ThreatResponse.DenyRules, options.ThreatResponsePolicy.Rules.DenyRules)
            .Select(rule => CreateThreatResponseRule(rule, nameof(ThreatResponsePolicy), "ThreatResponse.DenyRules", "Deny", "PolicyEngine.Rules.ThreatResponse.DenyRules"))
            .ToArray();
        var threatFallbackRules = EnabledFallback(options.Rules.ThreatResponse.Fallback, options.ThreatResponsePolicy.Rules.Fallback)
            .Select(rule => CreateThreatResponseRule(rule, nameof(ThreatResponsePolicy), "ThreatResponse.Fallback", "Fallback", "PolicyEngine.Rules.ThreatResponse.Fallback", isFallback: true))
            .ToArray();
        var targetPriorityRules = Merge(options.Rules.TargetPrioritization.PriorityRules, options.TargetPrioritizationPolicy.Rules.PriorityRules)
            .Select(rule => CreateTargetPriorityRule(rule, nameof(TargetPrioritizationPolicy), "TargetPrioritization.PriorityRules", "Prioritize", "PolicyEngine.Rules.TargetPrioritization.PriorityRules"))
            .ToArray();
        var targetDenyRules = Merge(options.Rules.TargetPrioritization.DenyRules, options.TargetPrioritizationPolicy.Rules.DenyRules)
            .Select(rule => CreateTargetPriorityRule(rule, nameof(TargetPrioritizationPolicy), "TargetPrioritization.DenyRules", "Deny", "PolicyEngine.Rules.TargetPrioritization.DenyRules"))
            .ToArray();
        var targetFallbackRules = EnabledFallback(options.Rules.TargetPrioritization.Fallback, options.TargetPrioritizationPolicy.Rules.Fallback)
            .Select(rule => CreateTargetPriorityRule(rule, nameof(TargetPrioritizationPolicy), "TargetPrioritization.Fallback", "Fallback", "PolicyEngine.Rules.TargetPrioritization.Fallback", isFallback: true))
            .ToArray();
        var resourceRules = Merge(options.Rules.ResourceUsage.Rules, options.ResourceUsagePolicy.Rules.Rules)
            .Select(rule => CreateResourceUsageRule(rule, nameof(ResourceUsagePolicy), "ResourceUsage.Rules", "Resource", "PolicyEngine.Rules.ResourceUsage.Rules"))
            .ToArray();
        var resourceFallbackRules = EnabledFallback(options.Rules.ResourceUsage.Fallback, options.ResourceUsagePolicy.Rules.Fallback)
            .Select(rule => CreateResourceUsageRule(rule, nameof(ResourceUsagePolicy), "ResourceUsage.Fallback", "Fallback", "PolicyEngine.Rules.ResourceUsage.Fallback", isFallback: true))
            .ToArray();
        var transitRules = Merge(options.Rules.Transit.Rules, options.TransitPolicy.Rules.Rules)
            .Select(rule => CreateTransitRule(rule, nameof(TransitPolicy), "Transit.Rules", "Transit", "PolicyEngine.Rules.Transit.Rules"))
            .ToArray();
        var transitFallbackRules = EnabledFallback(options.Rules.Transit.Fallback, options.TransitPolicy.Rules.Fallback)
            .Select(rule => CreateTransitRule(rule, nameof(TransitPolicy), "Transit.Fallback", "Fallback", "PolicyEngine.Rules.Transit.Fallback", isFallback: true))
            .ToArray();
        var abortRules = Merge(options.Rules.Abort.Rules, options.AbortPolicy.Rules.Rules)
            .Select(rule => CreateAbortRule(rule, nameof(AbortPolicy), "Abort.Rules", "Abort", "PolicyEngine.Rules.Abort.Rules"))
            .ToArray();
        var abortFallbackRules = EnabledFallback(options.Rules.Abort.Fallback, options.AbortPolicy.Rules.Fallback)
            .Select(rule => CreateAbortRule(rule, nameof(AbortPolicy), "Abort.Fallback", "Fallback", "PolicyEngine.Rules.Abort.Fallback", isFallback: true))
            .ToArray();

        return new PolicyRuleSet(
            siteAllowRules,
            siteFallbackRules,
            threatRetreatRules,
            threatDenyRules,
            threatFallbackRules,
            targetPriorityRules,
            targetDenyRules,
            targetFallbackRules,
            resourceRules,
            resourceFallbackRules,
            transitRules,
            transitFallbackRules,
            abortRules,
            abortFallbackRules);
    }

    private static IEnumerable<T> Merge<T>(IReadOnlyList<T> topLevel, IReadOnlyList<T> policyLevel)
        where T : PolicyRuleOptions =>
        topLevel.Count > 0 ? topLevel.Where(static rule => rule.Enabled) : policyLevel.Where(static rule => rule.Enabled);

    private static IEnumerable<PolicyRuleOptions> EnabledFallback(FallbackRuleOptions topLevel, FallbackRuleOptions policyLevel) =>
        (topLevel.Enabled ? topLevel : policyLevel.Enabled ? policyLevel : null) is { } rule
            ? [rule]
            : [];

    private static IEnumerable<PolicyRuleOptions> BuildSiteSelectionFallback(PolicyEngineOptions options)
    {
        if (options.Rules.SiteSelection.Fallback.Enabled || options.SelectNextSitePolicy.Rules.Fallback.Enabled)
        {
            return EnabledFallback(options.Rules.SiteSelection.Fallback, options.SelectNextSitePolicy.Rules.Fallback);
        }

        if (!options.Rules.SiteSelection.IgnoreNonAllowlistedSites)
        {
            return
            [
                new FallbackRuleOptions
                {
                    RuleName = "no-allowed-site",
                    DirectiveKind = options.Rules.SiteSelection.NoAllowedCandidateDirectiveKind,
                    Priority = options.Rules.SiteSelection.NoAllowedCandidatePriority,
                    SuggestedPolicy = options.Rules.SiteSelection.NoAllowedCandidateDirectiveKind,
                    Blocks = options.Rules.SiteSelection.NoAllowedCandidateDirectiveKind is "Wait" or "PauseActivity",
                    MinimumWaitMs = options.Rules.SiteSelection.MinimumWaitMs,
                    Reason = "No configured site-selection rule accepted the candidate."
                }
            ];
        }

        return [];
    }

    private static SiteSelectionRule CreateSiteSelectionRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new SiteSelectionRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static ThreatResponseRule CreateThreatResponseRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new ThreatResponseRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static TargetPriorityRule CreateTargetPriorityRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new TargetPriorityRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static ResourceUsageRule CreateResourceUsageRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new ResourceUsageRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static TransitRule CreateTransitRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new TransitRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static AbortRule CreateAbortRule(
        PolicyRuleOptions rule,
        string policyName,
        string ruleFamily,
        string ruleIntent,
        string sourceScope,
        bool isFallback = false)
    {
        var values = Values(rule);
        return new AbortRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason)
        {
            PolicyName = policyName,
            RuleFamily = ruleFamily,
            RuleIntent = ruleIntent,
            SourceScope = sourceScope,
            IsFallback = isFallback
        };
    }

    private static PolicyRuleValues Values(PolicyRuleOptions rule)
    {
        if (!Enum.TryParse<DecisionDirectiveKind>(rule.DirectiveKind, ignoreCase: true, out var directiveKind))
        {
            directiveKind = DecisionDirectiveKind.Observe;
        }

        return new PolicyRuleValues(
            rule.RuleName.Trim(),
            Clean(rule.MatchLabels),
            rule.LabelMatchMode,
            Clean(rule.MatchTypes),
            rule.TypeMatchMode,
            Clean(rule.MatchTags),
            rule.RequireAllTags,
            rule.AllowedThreatSeverities,
            rule.MinThreatSeverity,
            rule.MinRiskSeverity,
            rule.MatchSuggestedPolicies,
            rule.MatchSessionStatuses,
            rule.MatchNavigationStatuses,
            rule.RequireTransitioning,
            rule.RequireDestination,
            rule.RequireIdleNavigation,
            rule.RequireIdleActivity,
            rule.RequireNoActiveTarget,
            rule.RequireActiveTarget,
            rule.MatchResourceCritical,
            rule.MatchResourceDegraded,
            rule.RequireDefensivePosture,
            rule.MinProgressPercent,
            rule.MaxProgressPercent,
            rule.MinResourcePercent,
            rule.MaxResourcePercent,
            rule.MinWarningCount,
            rule.MaxWarningCount,
            rule.MinUnknownCount,
            rule.MaxUnknownCount,
            rule.MinAvailableCount,
            rule.MaxAvailableCount,
            rule.MinConfidence,
            rule.MaxConfidence,
            string.IsNullOrWhiteSpace(rule.MetricName) ? null : rule.MetricName.Trim(),
            rule.MinMetricValue,
            rule.MaxMetricValue,
            directiveKind,
            rule.Priority,
            rule.SuggestedPolicy.Trim(),
            rule.Blocks,
            rule.Aborts,
            TimeSpan.FromMilliseconds(rule.MinimumWaitMs),
            string.IsNullOrWhiteSpace(rule.ThresholdName) ? null : rule.ThresholdName.Trim(),
            string.IsNullOrWhiteSpace(rule.PolicyMode) ? null : rule.PolicyMode.Trim(),
            string.IsNullOrWhiteSpace(rule.TargetLabelTemplate) ? null : rule.TargetLabelTemplate.Trim(),
            rule.Reason.Trim());
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

    private sealed record PolicyRuleValues(
        string RuleName,
        IReadOnlyList<string> MatchLabels,
        PolicyRuleMatchMode LabelMatchMode,
        IReadOnlyList<string> MatchTypes,
        PolicyRuleMatchMode TypeMatchMode,
        IReadOnlyList<string> MatchTags,
        bool RequireAllTags,
        IReadOnlyList<ThreatSeverity> AllowedThreatSeverities,
        ThreatSeverity? MinThreatSeverity,
        RiskSeverity? MinRiskSeverity,
        IReadOnlyList<RiskPolicySuggestion> MatchSuggestedPolicies,
        IReadOnlyList<SessionStatus> MatchSessionStatuses,
        IReadOnlyList<NavigationStatus> MatchNavigationStatuses,
        bool? RequireTransitioning,
        bool RequireDestination,
        bool RequireIdleNavigation,
        bool RequireIdleActivity,
        bool RequireNoActiveTarget,
        bool RequireActiveTarget,
        bool? MatchResourceCritical,
        bool? MatchResourceDegraded,
        bool RequireDefensivePosture,
        double? MinProgressPercent,
        double? MaxProgressPercent,
        double? MinResourcePercent,
        double? MaxResourcePercent,
        int? MinWarningCount,
        int? MaxWarningCount,
        int? MinUnknownCount,
        int? MaxUnknownCount,
        int? MinAvailableCount,
        int? MaxAvailableCount,
        double? MinConfidence,
        double? MaxConfidence,
        string? MetricName,
        double? MinMetricValue,
        double? MaxMetricValue,
        DecisionDirectiveKind DirectiveKind,
        int Priority,
        string SuggestedPolicy,
        bool Blocks,
        bool Aborts,
        TimeSpan MinimumWait,
        string? ThresholdName,
        string? PolicyMode,
        string? TargetLabelTemplate,
        string Reason);
}
