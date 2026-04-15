using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Policy;

public sealed class DefaultDecisionPlanAggregator : IDecisionPlanAggregator
{
    private readonly SessionHostOptions _options;

    public DefaultDecisionPlanAggregator(SessionHostOptions options)
    {
        _options = options;
    }

    public DecisionPlan Aggregate(
        SessionId sessionId,
        DateTimeOffset plannedAtUtc,
        IReadOnlyList<PolicyEvaluationResult> policyResults)
    {
        var options = _options.PolicyEngine;
        var producedDirectives = policyResults
            .SelectMany(static result => result.Directives)
            .Where(directive => directive.Priority >= options.MinDirectivePriority)
            .OrderByDescending(static directive => directive.Priority)
            .ThenBy(static directive => directive.SourcePolicy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static directive => directive.DirectiveKind)
            .ThenBy(static directive => directive.DirectiveId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var directives = RemoveDuplicates(producedDirectives);
        var suppressedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        directives = ApplySuppressionRules(directives, options.AggregationRules.SuppressionRules, suppressedCounts);

        directives = directives
            .OrderByDescending(static directive => directive.Priority)
            .ThenBy(static directive => directive.SourcePolicy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static directive => directive.DirectiveKind)
            .ThenBy(static directive => directive.DirectiveId, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxReturnedDirectives)
            .ToArray();

        var reasons = policyResults
            .SelectMany(static result => result.Reasons)
            .Concat(directives.SelectMany(static directive => directive.Reasons))
            .Distinct()
            .ToArray();
        var warnings = policyResults.SelectMany(static result => result.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var summary = new PolicyExecutionSummary(
            policyResults.Select(static result => result.PolicyName).ToArray(),
            policyResults.Where(static result => result.DidMatch).Select(static result => result.PolicyName).ToArray(),
            policyResults.Where(static result => result.DidBlock).Select(static result => result.PolicyName).ToArray(),
            policyResults.Where(static result => result.DidAbort).Select(static result => result.PolicyName).ToArray(),
            producedDirectives.Length,
            directives.Length,
            suppressedCounts);

        return new DecisionPlan(
            sessionId,
            plannedAtUtc,
            ResolveStatus(directives, policyResults, options.AggregationRules.StatusRules),
            directives,
            reasons,
            summary,
            warnings);
    }

    private static DecisionDirective[] RemoveDuplicates(IReadOnlyList<DecisionDirective> directives) =>
        directives
            .GroupBy(static directive => new
            {
                directive.DirectiveKind,
                Target = directive.TargetId ?? directive.TargetLabel ?? string.Empty,
                directive.SourcePolicy
            })
            .Select(static group => group.OrderByDescending(static directive => directive.Priority).ThenBy(static directive => directive.DirectiveId, StringComparer.OrdinalIgnoreCase).First())
            .ToArray();

    private static DecisionDirective[] Suppress(
        IReadOnlyList<DecisionDirective> directives,
        Func<DecisionDirective, bool> shouldSuppress,
        string reason,
        IDictionary<string, int> suppressedCounts)
    {
        var retained = new List<DecisionDirective>(directives.Count);
        var suppressed = 0;

        foreach (var directive in directives)
        {
            if (shouldSuppress(directive))
            {
                suppressed++;
                continue;
            }

            retained.Add(directive);
        }

        if (suppressed > 0)
        {
            suppressedCounts[reason] = suppressedCounts.TryGetValue(reason, out var current)
                ? current + suppressed
                : suppressed;
        }

        return retained.ToArray();
    }

    private static DecisionDirective[] ApplySuppressionRules(
        IReadOnlyList<DecisionDirective> directives,
        IReadOnlyList<DirectiveSuppressionRuleOptions> rules,
        IDictionary<string, int> suppressedCounts)
    {
        var current = directives.ToArray();

        foreach (var rule in rules.Where(static rule => rule.Enabled))
        {
            var triggerKinds = ParseDirectiveKinds(rule.TriggerDirectiveKinds);
            var suppressedKinds = ParseDirectiveKinds(rule.SuppressedDirectiveKinds);
            var preserveKinds = ParseDirectiveKinds(rule.PreserveDirectiveKinds);
            var blockedByKinds = ParseDirectiveKinds(rule.BlockedByDirectiveKinds);
            var wildcardSuppression = rule.SuppressedDirectiveKinds.Any(static value => string.Equals(value, "*", StringComparison.Ordinal));
            var triggers = current
                .Where(directive => triggerKinds.Contains(directive.DirectiveKind))
                .ToArray();

            if (triggers.Length == 0)
            {
                continue;
            }

            if (blockedByKinds.Count > 0)
            {
                var strongestBlockingPriority = current
                    .Where(directive => blockedByKinds.Contains(directive.DirectiveKind))
                    .Select(static directive => directive.Priority)
                    .DefaultIfEmpty(0)
                    .Max();
                var strongestTriggerPriority = triggers.Max(static directive => directive.Priority);

                if (strongestBlockingPriority > strongestTriggerPriority)
                {
                    continue;
                }
            }

            current = Suppress(
                current,
                directive => ShouldSuppressDirective(rule, triggers, wildcardSuppression, suppressedKinds, preserveKinds, directive),
                rule.RuleName,
                suppressedCounts);
        }

        return current;
    }

    private static bool ShouldSuppressDirective(
        DirectiveSuppressionRuleOptions rule,
        IReadOnlyList<DecisionDirective> triggers,
        bool wildcardSuppression,
        IReadOnlySet<DecisionDirectiveKind> suppressedKinds,
        IReadOnlySet<DecisionDirectiveKind> preserveKinds,
        DecisionDirective directive)
    {
        if (preserveKinds.Contains(directive.DirectiveKind))
        {
            return false;
        }

        if (!wildcardSuppression && !suppressedKinds.Contains(directive.DirectiveKind))
        {
            return false;
        }

        if (!rule.SuppressLowerPriorityOnly)
        {
            return true;
        }

        return triggers.Any(trigger => directive.Priority < trigger.Priority);
    }

    private static DecisionPlanStatus ResolveStatus(
        IReadOnlyList<DecisionDirective> directives,
        IReadOnlyList<PolicyEvaluationResult> policyResults,
        IReadOnlyList<DecisionPlanStatusRuleOptions> rules)
    {
        foreach (var rule in rules.Where(static rule => rule.Enabled))
        {
            var directiveKinds = ParseDirectiveKinds(rule.DirectiveKinds);
            var matchedDirective = directiveKinds.Count > 0 &&
                directives.Any(directive => directiveKinds.Contains(directive.DirectiveKind));
            var matchedPolicyAbort = rule.IncludePolicyAbortFlag &&
                policyResults.Any(static result => result.DidAbort);

            if (matchedDirective || matchedPolicyAbort)
            {
                return ParsePlanStatus(rule.Status);
            }
        }

        return directives.Count == 0 ? DecisionPlanStatus.Idle : DecisionPlanStatus.Ready;
    }

    private static IReadOnlySet<DecisionDirectiveKind> ParseDirectiveKinds(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.Equals(value, "*", StringComparison.Ordinal))
            .Select(static value => Enum.Parse<DecisionDirectiveKind>(value, ignoreCase: true))
            .ToHashSet();

    private static DecisionPlanStatus ParsePlanStatus(string value) =>
        Enum.Parse<DecisionPlanStatus>(value, ignoreCase: true);
}
