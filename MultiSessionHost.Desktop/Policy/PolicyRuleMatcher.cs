using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Policy;

public interface IPolicyRuleMatcher
{
    bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria);
}

public sealed class DefaultPolicyRuleMatcher : IPolicyRuleMatcher
{
    public bool IsMatch(PolicyRule rule, PolicyRuleCandidate candidate, out IReadOnlyList<string> matchedCriteria)
    {
        var criteria = new List<string>();

        if (!MatchText(rule.MatchLabels, rule.LabelMatchMode, candidate.Label, "label", criteria) ||
            !MatchText(rule.MatchTypes, rule.TypeMatchMode, candidate.Type, "type", criteria) ||
            !MatchTags(rule, candidate, criteria) ||
            !MatchEnumSet(rule.AllowedThreatSeverities, candidate.ThreatSeverity, "allowedThreatSeverity", criteria) ||
            !MatchMinimum(rule.MinThreatSeverity, candidate.ThreatSeverity, "minThreatSeverity", criteria) ||
            !MatchMinimum(rule.MinRiskSeverity, candidate.RiskSeverity, "minRiskSeverity", criteria) ||
            !MatchEnumSet(rule.MatchSuggestedPolicies, candidate.SuggestedPolicy, "suggestedPolicy", criteria) ||
            !MatchEnumSet(rule.MatchSessionStatuses, candidate.SessionStatus, "sessionStatus", criteria) ||
            !MatchEnumSet(rule.MatchNavigationStatuses, candidate.NavigationStatus, "navigationStatus", criteria) ||
            !MatchBool(rule.RequireTransitioning, candidate.IsTransitioning, "transitioning", criteria) ||
            !MatchRequired(rule.RequireDestination, candidate.HasDestination, "destination", criteria) ||
            !MatchRequired(rule.RequireIdleNavigation, candidate.IsNavigationIdle, "idleNavigation", criteria) ||
            !MatchRequired(rule.RequireIdleActivity, candidate.IsActivityIdle, "idleActivity", criteria) ||
            !MatchRequired(rule.RequireNoActiveTarget, !candidate.HasActiveTarget, "noActiveTarget", criteria) ||
            !MatchRequired(rule.RequireActiveTarget, candidate.HasActiveTarget, "activeTarget", criteria) ||
            !MatchBool(rule.MatchResourceCritical, candidate.ResourceCritical, "resourceCritical", criteria) ||
            !MatchBool(rule.MatchResourceDegraded, candidate.ResourceDegraded, "resourceDegraded", criteria) ||
            !MatchRequired(rule.RequireDefensivePosture, candidate.DefensivePostureActive, "defensivePosture", criteria) ||
            !MatchRange(rule.MinProgressPercent, rule.MaxProgressPercent, candidate.ProgressPercent, "progressPercent", criteria) ||
            !MatchRange(rule.MinResourcePercent, rule.MaxResourcePercent, candidate.ResourcePercent, "resourcePercent", criteria) ||
            !MatchRange(rule.MinWarningCount, rule.MaxWarningCount, candidate.WarningCount, "warningCount", criteria) ||
            !MatchRange(rule.MinUnknownCount, rule.MaxUnknownCount, candidate.UnknownCount, "unknownCount", criteria) ||
            !MatchRange(rule.MinAvailableCount, rule.MaxAvailableCount, candidate.AvailableCount, "availableCount", criteria) ||
            !MatchRange(rule.MinConfidence, rule.MaxConfidence, candidate.Confidence, "confidence", criteria) ||
            !MatchMetric(rule, candidate, criteria))
        {
            matchedCriteria = criteria;
            return false;
        }

        matchedCriteria = criteria;
        return true;
    }

    private static bool MatchText(IReadOnlyList<string> matchers, PolicyRuleMatchMode mode, string? value, string name, List<string> criteria)
    {
        if (matchers.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
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

        return matched;
    }

    private static bool MatchTags(PolicyRule rule, PolicyRuleCandidate candidate, List<string> criteria)
    {
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

        return matched;
    }

    private static bool MatchEnumSet<T>(IReadOnlyList<T> values, T actual, string name, List<string> criteria)
        where T : struct, Enum
    {
        if (values.Count == 0)
        {
            return true;
        }

        var matched = values.Contains(actual);

        if (matched)
        {
            criteria.Add(name);
        }

        return matched;
    }

    private static bool MatchMinimum<T>(T? minimum, T actual, string name, List<string> criteria)
        where T : struct, Enum
    {
        if (minimum is null)
        {
            return true;
        }

        var matched = Convert.ToInt32(actual) >= Convert.ToInt32(minimum.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        return matched;
    }

    private static bool MatchBool(bool? expected, bool actual, string name, List<string> criteria)
    {
        if (expected is null)
        {
            return true;
        }

        var matched = expected.Value == actual;

        if (matched)
        {
            criteria.Add(name);
        }

        return matched;
    }

    private static bool MatchRequired(bool required, bool actual, string name, List<string> criteria)
    {
        if (!required)
        {
            return true;
        }

        if (actual)
        {
            criteria.Add(name);
        }

        return actual;
    }

    private static bool MatchRange(double? min, double? max, double? actual, string name, List<string> criteria)
    {
        if (min is null && max is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        var matched = (min is null || actual.Value >= min.Value) && (max is null || actual.Value <= max.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        return matched;
    }

    private static bool MatchRange(int? min, int? max, int? actual, string name, List<string> criteria)
    {
        if (min is null && max is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        var matched = (min is null || actual.Value >= min.Value) && (max is null || actual.Value <= max.Value);

        if (matched)
        {
            criteria.Add(name);
        }

        return matched;
    }

    private static bool MatchMetric(PolicyRule rule, PolicyRuleCandidate candidate, List<string> criteria)
    {
        if (string.IsNullOrWhiteSpace(rule.MetricName))
        {
            return true;
        }

        if (!candidate.Metrics.TryGetValue(rule.MetricName, out var value))
        {
            return false;
        }

        var matched = (rule.MinMetricValue is null || value >= rule.MinMetricValue.Value) &&
            (rule.MaxMetricValue is null || value <= rule.MaxMetricValue.Value);

        if (matched)
        {
            criteria.Add("metric:" + rule.MetricName);
        }

        return matched;
    }
}
