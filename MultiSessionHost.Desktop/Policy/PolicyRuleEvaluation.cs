namespace MultiSessionHost.Desktop.Policy;

internal static class PolicyRuleEvaluation
{
    public static bool TryApplyFirst(
        PolicyResultBuilder builder,
        IPolicyRuleMatcher matcher,
        IReadOnlyList<PolicyRule> rules,
        IReadOnlyList<PolicyRuleCandidate> candidates,
        DateTimeOffset now,
        Func<PolicyRuleCandidate, string?>? targetIdSelector = null)
    {
        foreach (var rule in rules)
        {
            if (candidates.Count == 0)
            {
                builder.AddRuleTrace(rule, candidate: null, PolicyRuleEvaluationOutcome.Skipped, rejectedReason: "No candidates were available.");
                continue;
            }

            string? rejectedReason = null;
            IReadOnlyList<string> rejectedCriteria = [];
            PolicyRuleCandidate? rejectedCandidate = null;

            foreach (var candidate in candidates)
            {
                if (matcher.IsMatch(rule, candidate, out var matchedCriteria, out var candidateRejectedReason))
                {
                    builder.AddRuleTrace(
                        rule,
                        candidate,
                        PolicyRuleEvaluationOutcome.Matched,
                        matchedCriteria,
                        producedDirectiveKinds: [rule.DirectiveKind.ToString()]);
                    PolicyDirectiveFactory.AddDirective(
                        builder,
                        rule,
                        candidate,
                        matchedCriteria,
                        now,
                        targetIdSelector?.Invoke(candidate) ?? candidate.CandidateId);
                    return true;
                }

                rejectedReason ??= candidateRejectedReason;
                rejectedCriteria = matchedCriteria;
                rejectedCandidate ??= candidate;
            }

            builder.AddRuleTrace(
                rule,
                rejectedCandidate,
                PolicyRuleEvaluationOutcome.Rejected,
                rejectedCriteria,
                rejectedReason ?? "No candidate matched rule.");
        }

        return false;
    }

    public static IReadOnlyList<PolicyRuleCandidate> WithFallbackCandidate(
        IReadOnlyList<PolicyRuleCandidate> candidates,
        PolicyEvaluationContext context,
        string policyName) =>
        candidates.Count > 0 ? candidates : [PolicyCandidateFactory.CreateFallback(context, policyName)];

    public static string CandidateSummary(IReadOnlyList<PolicyRuleCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "0 candidates";
        }

        return string.Join(
            ", ",
            candidates
                .Take(5)
                .Select(static candidate => string.IsNullOrWhiteSpace(candidate.Label)
                    ? candidate.CandidateId
                    : candidate.CandidateId + ":" + candidate.Label));
    }
}
