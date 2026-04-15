namespace MultiSessionHost.Desktop.Policy;

internal static class PolicyDirectiveFactory
{
    public static void AddDirective(
        PolicyResultBuilder builder,
        PolicyRule rule,
        PolicyRuleCandidate candidate,
        IReadOnlyList<string> matchedCriteria,
        DateTimeOffset now,
        string? targetId)
    {
        builder.AddReason(
            rule.RuleName,
            rule.Reason,
            PolicyHelpers.Metadata(
                ("policyRuleFamily", rule.RuleFamily),
                ("ruleIntent", rule.RuleIntent),
                ("isFallback", rule.IsFallback.ToString())));
        builder.AddDirective(
            rule.DirectiveKind,
            rule.Priority,
            targetId,
            PolicyHelpers.ResolveTargetLabel(rule, candidate),
            rule.SuggestedPolicy,
            PolicyHelpers.RuleMetadata(rule, candidate, matchedCriteria, now),
            rule.Blocks,
            rule.Aborts);
    }
}
