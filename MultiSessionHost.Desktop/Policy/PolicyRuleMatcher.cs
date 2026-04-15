using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public interface IPolicyRuleMatcher
{
    bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria);

    bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria, out string? rejectedReason);
}

public sealed class DefaultPolicyRuleMatcher : IPolicyRuleMatcher
{
    public bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria) =>
        IsMatch(rule, candidate, out matchedCriteria, out _);

    public bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria, out string? rejectedReason)
    {
        var criteria = new List<string>();

        if (!MatchText(rule.MatchLabels, rule.LabelMatchMode, candidate.Label, "label", criteria, out rejectedReason) ||
            !MatchText(rule.MatchTypes, rule.TypeMatchMode, candidate.Type, "type", criteria, out rejectedReason) ||
            !MatchTags(rule, candidate, criteria, out rejectedReason) ||
            !MatchEnumSet(rule.AllowedThreatSeverities, candidate.ThreatSeverity, "allowedThreatSeverity", criteria, out rejectedReason) ||
            !MatchMinimum(rule.MinThreatSeverity, candidate.ThreatSeverity, "minThreatSeverity", criteria, out rejectedReason) ||
            !MatchMinimum(rule.MinRiskSeverity, candidate.RiskSeverity, "minRiskSeverity", criteria, out rejectedReason) ||
            !MatchEnumSet(rule.MatchSuggestedPolicies, candidate.SuggestedPolicy, "suggestedPolicy", criteria, out rejectedReason) ||
            !MatchEnumSet(rule.MatchSessionStatuses, candidate.SessionStatus, "sessionStatus", criteria, out rejectedReason) ||
            !MatchEnumSet(rule.MatchNavigationStatuses, candidate.NavigationStatus, "navigationStatus", criteria, out rejectedReason) ||
            !MatchBool(rule.RequireTransitioning, candidate.IsTransitioning, "transitioning", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireDestination, candidate.HasDestination, "destination", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireIdleNavigation, candidate.IsNavigationIdle, "idleNavigation", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireIdleActivity, candidate.IsActivityIdle, "idleActivity", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireNoActiveTarget, !candidate.HasActiveTarget, "noActiveTarget", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireActiveTarget, candidate.HasActiveTarget, "activeTarget", criteria, out rejectedReason) ||
            !MatchBool(rule.MatchResourceCritical, candidate.ResourceCritical, "resourceCritical", criteria, out rejectedReason) ||
            !MatchBool(rule.MatchResourceDegraded, candidate.ResourceDegraded, "resourceDegraded", criteria, out rejectedReason) ||
            !MatchRequired(rule.RequireDefensivePosture, candidate.DefensivePostureActive, "defensivePosture", criteria, out rejectedReason) ||
            !MatchRange(rule.MinProgressPercent, rule.MaxProgressPercent, candidate.ProgressPercent, "progressPercent", criteria, out rejectedReason) ||
            !MatchRange(rule.MinResourcePercent, rule.MaxResourcePercent, candidate.ResourcePercent, "resourcePercent", criteria, out rejectedReason) ||
            !MatchRange(rule.MinWarningCount, rule.MaxWarningCount, candidate.WarningCount, "warningCount", criteria, out rejectedReason) ||
            !MatchRange(rule.MinUnknownCount, rule.MaxUnknownCount, candidate.UnknownCount, "unknownCount", criteria, out rejectedReason) ||
            !MatchRange(rule.MinAvailableCount, rule.MaxAvailableCount, candidate.AvailableCount, "availableCount", criteria, out rejectedReason) ||
            !MatchRange(rule.MinConfidence, rule.MaxConfidence, candidate.Confidence, "confidence", criteria, out rejectedReason) ||
            !MatchMetric(rule, candidate, criteria, out rejectedReason))
        {
            matchedCriteria = criteria;
            return false;
        }

        matchedCriteria = criteria;
        rejectedReason = null;
        return true;
    }

    private static bool MatchText(IReadOnlyList<string> matchers, PolicyRuleMatchMode mode, string? value, string name, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (matchers.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            rejectedReason = $"{name} was empty.";
            return false;
        }

        var matched = matchers.Any(matcher => mode switch
        {
            PolicyRuleMatchMode.Exact => string.Equals(value, matcher, StringComparison.OrdinalIgnoreCase),
            PolicyRuleMatchMode.Contains => value.Contains(matcher, StringComparison.OrdinalIgnoreCase),
            PolicyRuleMatchMode.StartsWith => value.StartsWith(matcher, StringComparison.OrdinalIgnoreCase),
            PolicyRuleMatchMode.EndsWith => value.EndsWith(matcher, StringComparison.OrdinalIgnoreCase),
            _ => false
        });

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} did not match configured values.";
        return matched;
    }

    private static bool MatchTags(PolicyRule rule, PolicyRuleCandidate candidate, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (rule.MatchTags.Count == 0)
        {
            return true;
        }

        var tags = candidate.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = rule.RequireAllTags
            ? rule.MatchTags.All(tags.Contains)
            : rule.MatchTags.Any(tags.Contains);

        if (matched)
        {
            criteria.Add("tags");
        }

        rejectedReason = matched ? null : "tags did not match configured values.";
        return matched;
    }

    private static bool MatchEnumSet<T>(IReadOnlyList<T> values, T actual, string name, List<string> criteria, out string? rejectedReason)
        where T : struct, Enum
    {
        rejectedReason = null;
        if (values.Count == 0)
        {
            return true;
        }

        var matched = values.Contains(actual);

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} did not match configured values.";
        return matched;
    }

    private static bool MatchMinimum<T>(T? minimum, T actual, string name, List<string> criteria, out string? rejectedReason)
        where T : struct, Enum
    {
        rejectedReason = null;
        if (minimum is null)
        {
            return true;
        }

        var matched = Convert.ToInt32(actual) >= Convert.ToInt32(minimum.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} was below configured minimum.";
        return matched;
    }

    private static bool MatchBool(bool? expected, bool actual, string name, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (expected is null)
        {
            return true;
        }

        var matched = expected.Value == actual;

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} did not match configured value.";
        return matched;
    }

    private static bool MatchRequired(bool required, bool actual, string name, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (!required)
        {
            return true;
        }

        if (actual)
        {
            criteria.Add(name);
        }

        rejectedReason = actual ? null : $"{name} was required but not present.";
        return actual;
    }

    private static bool MatchRange(double? min, double? max, double? actual, string name, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (min is null && max is null)
        {
            return true;
        }

        if (actual is null)
        {
            rejectedReason = $"{name} was unavailable.";
            return false;
        }

        var matched = (min is null || actual.Value >= min.Value) && (max is null || actual.Value <= max.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} was outside configured range.";
        return matched;
    }

    private static bool MatchRange(int? min, int? max, int? actual, string name, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (min is null && max is null)
        {
            return true;
        }

        if (actual is null)
        {
            rejectedReason = $"{name} was unavailable.";
            return false;
        }

        var matched = (min is null || actual.Value >= min.Value) && (max is null || actual.Value <= max.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        rejectedReason = matched ? null : $"{name} was outside configured range.";
        return matched;
    }

    private static bool MatchMetric(PolicyRule rule, PolicyRuleCandidate candidate, List<string> criteria, out string? rejectedReason)
    {
        rejectedReason = null;
        if (string.IsNullOrWhiteSpace(rule.MetricName))
        {
            return true;
        }

        if (!candidate.Metrics.TryGetValue(rule.MetricName, out var value))
        {
            rejectedReason = $"metric:{rule.MetricName} was unavailable.";
            return false;
        }

        var matched = (rule.MinMetricValue is null || value >= rule.MinMetricValue.Value) &&
            (rule.MaxMetricValue is null || value <= rule.MaxMetricValue.Value);

        if (matched)
        {
            criteria.Add("metric:" + rule.MetricName);
        }

        rejectedReason = matched ? null : $"metric:{rule.MetricName} was outside configured range.";
        return matched;
    }
}
