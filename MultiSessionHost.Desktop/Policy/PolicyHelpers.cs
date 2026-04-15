using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

internal static class PolicyHelpers
{
    public static RiskEntityAssessment? GetTopThreat(RiskAssessmentResult? riskAssessment) =>
        riskAssessment?.Entities
            .Where(static entity => entity.Disposition == RiskDisposition.Threat)
            .OrderByDescending(static entity => entity.Priority)
            .ThenByDescending(static entity => entity.Severity)
            .ThenBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.CandidateId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static IReadOnlyDictionary<string, string> Metadata(params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    public static IReadOnlyDictionary<string, string> RuleMetadata(
        PolicyRule rule,
        PolicyRuleCandidate candidate,
        IReadOnlyList<string> matchedCriteria,
        DateTimeOffset now)
    {
        var metadata = new Dictionary<string, string>(candidate.Metadata, StringComparer.Ordinal)
        {
            ["matchedRuleName"] = rule.RuleName,
            ["reasonRuleName"] = rule.RuleName,
            ["matchedCriteria"] = string.Join(",", matchedCriteria),
            ["policyRuleFamily"] = rule.RuleFamily,
            ["policyName"] = rule.PolicyName,
            ["ruleIntent"] = rule.RuleIntent,
            ["sourceScope"] = rule.SourceScope,
            ["isFallback"] = rule.IsFallback.ToString()
        };

        if (rule.MinimumWait > TimeSpan.Zero)
        {
            metadata["minimumWaitMs"] = rule.MinimumWait.TotalMilliseconds.ToString("0");
            metadata["notBeforeUtc"] = now.Add(rule.MinimumWait).ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(rule.ThresholdName))
        {
            metadata["thresholdName"] = rule.ThresholdName;
        }

        if (!string.IsNullOrWhiteSpace(rule.PolicyMode))
        {
            metadata["policyMode"] = rule.PolicyMode;
        }

        return metadata;
    }

    public static string? ResolveTargetLabel(PolicyRule rule, PolicyRuleCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(rule.TargetLabelTemplate))
        {
            return candidate.Label;
        }

        return rule.TargetLabelTemplate
            .Replace("{siteLabel}", candidate.Label ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{label}", candidate.Label ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", candidate.Type ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
