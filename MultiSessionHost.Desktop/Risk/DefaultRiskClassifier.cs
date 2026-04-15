using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Risk;

public sealed class DefaultRiskClassifier : IRiskClassifier
{
    private readonly RiskClassificationOptions _options;

    public DefaultRiskClassifier(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.RiskClassification;
    }

    public IReadOnlyList<RiskEntityAssessment> Classify(IReadOnlyList<RiskCandidate> candidates, IReadOnlyList<RiskRule> rules)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rules);

        var maxReturned = Math.Max(1, _options.MaxReturnedEntities);

        return candidates
            .Select(candidate => Classify(candidate, rules))
            .OrderByDescending(static assessment => assessment.Priority)
            .ThenByDescending(static assessment => assessment.Severity)
            .ThenBy(static assessment => assessment.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxReturned)
            .ToArray();
    }

    private RiskEntityAssessment Classify(RiskCandidate candidate, IReadOnlyList<RiskRule> rules)
    {
        foreach (var rule in rules)
        {
            var match = TryMatch(candidate, rule);

            if (match is null)
            {
                continue;
            }

            return new RiskEntityAssessment(
                candidate.CandidateId,
                candidate.Source,
                candidate.Name,
                candidate.Type,
                candidate.Tags,
                rule.Disposition,
                rule.Severity,
                rule.Priority,
                rule.SuggestedPolicy,
                rule.RuleName,
                [rule.Reason, .. match.MatchedCriteria.Select(criterion => $"Matched {criterion}.")],
                candidate.Confidence,
                candidate.Metadata);
        }

        return new RiskEntityAssessment(
            candidate.CandidateId,
            candidate.Source,
            candidate.Name,
            candidate.Type,
            candidate.Tags,
            _options.DefaultUnknownDisposition,
            _options.DefaultUnknownSeverity,
            Priority: 0,
            _options.DefaultUnknownPolicy,
            MatchedRuleName: null,
            ["No explicit risk rule matched this candidate."],
            candidate.Confidence,
            candidate.Metadata);
    }

    private static RiskRuleMatch? TryMatch(RiskCandidate candidate, RiskRule rule)
    {
        var matchedCriteria = new List<string>();

        if (rule.MatchByName.Count > 0)
        {
            if (!rule.MatchByName.Any(value => IsTextMatch(candidate.Name, value, rule.NameMatchMode)))
            {
                return null;
            }

            matchedCriteria.Add("name");
        }

        if (rule.MatchByType.Count > 0)
        {
            if (!rule.MatchByType.Any(value => IsTextMatch(candidate.Type, value, rule.TypeMatchMode)))
            {
                return null;
            }

            matchedCriteria.Add("type");
        }

        if (rule.MatchByTags.Count > 0)
        {
            var matchedTags = rule.MatchByTags
                .Where(requiredTag => candidate.Tags.Any(candidateTag => string.Equals(candidateTag, requiredTag, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            var tagsMatched = rule.RequireAllTags
                ? matchedTags.Length == rule.MatchByTags.Count
                : matchedTags.Length > 0;

            if (!tagsMatched)
            {
                return null;
            }

            matchedCriteria.Add(rule.RequireAllTags ? "all tags" : "tag");
        }

        return matchedCriteria.Count == 0 ? null : new RiskRuleMatch(rule.RuleName, matchedCriteria, rule.Reason);
    }

    private static bool IsTextMatch(string candidateValue, string ruleValue, RiskRuleMatchMode mode) =>
        mode switch
        {
            RiskRuleMatchMode.Exact => string.Equals(candidateValue, ruleValue, StringComparison.OrdinalIgnoreCase),
            RiskRuleMatchMode.Contains => candidateValue.Contains(ruleValue, StringComparison.OrdinalIgnoreCase),
            RiskRuleMatchMode.StartsWith => candidateValue.StartsWith(ruleValue, StringComparison.OrdinalIgnoreCase),
            RiskRuleMatchMode.EndsWith => candidateValue.EndsWith(ruleValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
