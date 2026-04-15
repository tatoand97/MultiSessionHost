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

        if (options.BlockOnAbort && directives.Any(static directive => directive.DirectiveKind == DecisionDirectiveKind.Abort))
        {
            directives = Suppress(
                directives,
                static directive => directive.DirectiveKind != DecisionDirectiveKind.Abort,
                "AbortPolicy",
                suppressedCounts);
        }
        else
        {
            if (options.PreferThreatResponseOverSelection &&
                directives.Any(static directive => directive.DirectiveKind is DecisionDirectiveKind.Withdraw or DecisionDirectiveKind.PauseActivity))
            {
                directives = Suppress(
                    directives,
                    static directive => directive.DirectiveKind is DecisionDirectiveKind.SelectSite or DecisionDirectiveKind.Navigate or DecisionDirectiveKind.SelectTarget,
                    "ThreatResponse",
                    suppressedCounts);
            }

            var strongestThreatPriority = directives
                .Where(static directive => directive.DirectiveKind is DecisionDirectiveKind.Withdraw or DecisionDirectiveKind.PauseActivity or DecisionDirectiveKind.AvoidTarget)
                .Select(static directive => directive.Priority)
                .DefaultIfEmpty(0)
                .Max();

            var waitPriority = directives
                .Where(static directive => directive.DirectiveKind == DecisionDirectiveKind.Wait)
                .Select(static directive => directive.Priority)
                .DefaultIfEmpty(0)
                .Max();

            if (options.PreferTransitStability && waitPriority > 0 && strongestThreatPriority <= waitPriority)
            {
                directives = Suppress(
                    directives,
                    directive => directive.Priority < waitPriority &&
                        directive.DirectiveKind is DecisionDirectiveKind.SelectSite
                            or DecisionDirectiveKind.Navigate
                            or DecisionDirectiveKind.SelectTarget
                            or DecisionDirectiveKind.PrioritizeTarget
                            or DecisionDirectiveKind.UseResource,
                    "TransitStability",
                    suppressedCounts);
            }
        }

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
            ResolveStatus(directives, policyResults),
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

    private static DecisionPlanStatus ResolveStatus(
        IReadOnlyList<DecisionDirective> directives,
        IReadOnlyList<PolicyEvaluationResult> policyResults)
    {
        if (directives.Any(static directive => directive.DirectiveKind == DecisionDirectiveKind.Abort) ||
            policyResults.Any(static result => result.DidAbort))
        {
            return DecisionPlanStatus.Aborting;
        }

        if (directives.Any(static directive => directive.DirectiveKind is DecisionDirectiveKind.Withdraw or DecisionDirectiveKind.PauseActivity or DecisionDirectiveKind.Wait))
        {
            return DecisionPlanStatus.Blocked;
        }

        return directives.Count == 0 ? DecisionPlanStatus.Idle : DecisionPlanStatus.Ready;
    }
}
