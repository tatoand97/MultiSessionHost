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
        var siteRules = Merge(options.Rules.SiteSelection.AllowRules, options.SelectNextSitePolicy.Rules.AllowRules)
            .Select(CreateSiteSelectionRule)
            .ToArray();
        var threatRules = Merge(options.Rules.ThreatResponse.RetreatRules, options.ThreatResponsePolicy.Rules.RetreatRules).Cast<PolicyRuleOptions>()
            .Concat(Merge(options.Rules.ThreatResponse.DenyRules, options.ThreatResponsePolicy.Rules.DenyRules).Cast<PolicyRuleOptions>())
            .Select(CreateThreatResponseRule)
            .ToArray();
        var targetRules = Merge(options.Rules.TargetPrioritization.PriorityRules, options.TargetPrioritizationPolicy.Rules.PriorityRules).Cast<PolicyRuleOptions>()
            .Concat(Merge(options.Rules.TargetPrioritization.DenyRules, options.TargetPrioritizationPolicy.Rules.DenyRules).Cast<PolicyRuleOptions>())
            .Select(CreateTargetPriorityRule)
            .ToArray();
        var resourceRules = Merge(options.Rules.ResourceUsage.Rules, options.ResourceUsagePolicy.Rules.Rules)
            .Select(CreateResourceUsageRule)
            .ToArray();
        var transitRules = Merge(options.Rules.Transit.Rules, options.TransitPolicy.Rules.Rules)
            .Select(CreateTransitRule)
            .ToArray();
        var abortRules = Merge(options.Rules.Abort.Rules, options.AbortPolicy.Rules.Rules)
            .Select(CreateAbortRule)
            .ToArray();

        return new PolicyRuleSet(siteRules, threatRules, targetRules, resourceRules, transitRules, abortRules);
    }

    private static IEnumerable<T> Merge<T>(IReadOnlyList<T> topLevel, IReadOnlyList<T> policyLevel)
        where T : PolicyRuleOptions =>
        topLevel.Count > 0 ? topLevel.Where(static rule => rule.Enabled) : policyLevel.Where(static rule => rule.Enabled);

    private static SiteSelectionRule CreateSiteSelectionRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new SiteSelectionRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
    }

    private static ThreatResponseRule CreateThreatResponseRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new ThreatResponseRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
    }

    private static TargetPriorityRule CreateTargetPriorityRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new TargetPriorityRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
    }

    private static ResourceUsageRule CreateResourceUsageRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new ResourceUsageRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
    }

    private static TransitRule CreateTransitRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new TransitRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
    }

    private static AbortRule CreateAbortRule(PolicyRuleOptions rule)
    {
        var values = Values(rule);
        return new AbortRule(values.RuleName, values.MatchLabels, values.LabelMatchMode, values.MatchTypes, values.TypeMatchMode, values.MatchTags, values.RequireAllTags, values.AllowedThreatSeverities, values.MinThreatSeverity, values.MinRiskSeverity, values.MatchSuggestedPolicies, values.MatchSessionStatuses, values.MatchNavigationStatuses, values.RequireTransitioning, values.RequireDestination, values.RequireIdleNavigation, values.RequireIdleActivity, values.RequireNoActiveTarget, values.RequireActiveTarget, values.MatchResourceCritical, values.MatchResourceDegraded, values.RequireDefensivePosture, values.MinProgressPercent, values.MaxProgressPercent, values.MinResourcePercent, values.MaxResourcePercent, values.MinWarningCount, values.MaxWarningCount, values.MinUnknownCount, values.MaxUnknownCount, values.MinAvailableCount, values.MaxAvailableCount, values.MinConfidence, values.MaxConfidence, values.MetricName, values.MinMetricValue, values.MaxMetricValue, values.DirectiveKind, values.Priority, values.SuggestedPolicy, values.Blocks, values.Aborts, values.MinimumWait, values.ThresholdName, values.PolicyMode, values.TargetLabelTemplate, values.Reason);
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
