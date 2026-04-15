using MultiSessionHost.Core.Configuration;

namespace MultiSessionHost.Desktop.Risk;

public sealed class ConfiguredRiskRuleProvider : IRiskRuleProvider
{
    private readonly IReadOnlyList<RiskRule> _rules;

    public ConfiguredRiskRuleProvider(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var riskOptions = options.RiskClassification;

        if (!riskOptions.EnableRiskClassification)
        {
            _rules = [];
            return;
        }

        if (riskOptions.Rules.Count == 0)
        {
            throw new InvalidOperationException("Risk classification is enabled but no rules are configured.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<RiskRule>();

        foreach (var rule in riskOptions.Rules.Where(static rule => rule.Enabled))
        {
            var ruleName = RequireValue(rule.RuleName, "Risk rule RuleName is required.");

            if (!seenNames.Add(ruleName))
            {
                throw new InvalidOperationException($"Risk rule '{rule.RuleName}' is duplicated.");
            }

            var nameMatchers = Normalize(rule.MatchByName);
            var typeMatchers = Normalize(rule.MatchByType);
            var tagMatchers = Normalize(rule.MatchByTags);

            if (nameMatchers.Count == 0 && typeMatchers.Count == 0 && tagMatchers.Count == 0)
            {
                throw new InvalidOperationException($"Risk rule '{ruleName}' must define at least one name, type, or tag matcher.");
            }

            if (rule.Priority < 0 || rule.Priority > 1000)
            {
                throw new InvalidOperationException($"Risk rule '{ruleName}' must have Priority between 0 and 1000.");
            }

            rules.Add(
                new RiskRule(
                    ruleName,
                    nameMatchers,
                    rule.NameMatchMode,
                    typeMatchers,
                    rule.TypeMatchMode,
                    tagMatchers,
                    rule.RequireAllTags,
                    rule.Disposition,
                    rule.Severity,
                    rule.Priority,
                    rule.SuggestedPolicy,
                    string.IsNullOrWhiteSpace(rule.Reason)
                        ? $"Matched configured risk rule '{ruleName}'."
                        : rule.Reason.Trim()));
        }

        if (rules.Count == 0)
        {
            throw new InvalidOperationException("Risk classification is enabled but all configured rules are disabled.");
        }

        _rules = rules.ToArray();
    }

    public IReadOnlyList<RiskRule> GetActiveRules() => _rules;

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string RequireValue(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value.Trim();
    }
}
