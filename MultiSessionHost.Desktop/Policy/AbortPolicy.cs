using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class AbortPolicy : IPolicy
{
    private readonly SessionHostOptions _options;
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public AbortPolicy(SessionHostOptions options)
        : this(options, new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public AbortPolicy(SessionHostOptions options, IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _options = options;
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(AbortPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidate = PolicyCandidateFactory.CreateAbort(context);
        var candidates = new PolicyRuleCandidate[] { candidate };
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.AbortRules, candidates, context.Now) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.AbortFallbackRules, candidates, context.Now);

        ApplyMemoryInfluenceIfNeeded(builder, context);

        return ValueTask.FromResult(builder.Build());
    }

    private void ApplyMemoryInfluenceIfNeeded(PolicyResultBuilder builder, PolicyEvaluationContext context)
    {
        var memoryContext = context.MemoryContext;
        var memoryOptions = _options.PolicyEngine.MemoryDecisioning;
        if (!memoryOptions.EnableMemoryDecisioning || memoryContext is null)
        {
            return;
        }

        var abortOptions = memoryOptions.Abort;
        if (!MemoryInfluenceHelpers.HasRepeatedFailurePattern(memoryContext.OutcomeSummary, abortOptions))
        {
            return;
        }

        var boostedPausePriority = _options.PolicyEngine.AbortPolicy.PausePriority + abortOptions.MemoryReinforceAbortPriorityBoost;
        var boostedAbortPriority = _options.PolicyEngine.AbortPolicy.AbortPriority + abortOptions.MemoryReinforceAbortPriorityBoost;
        var shouldAbort = context.SessionSnapshot.Runtime.CurrentStatus == SessionStatus.Faulted;
        var directiveKind = shouldAbort ? DecisionDirectiveKind.Abort : DecisionDirectiveKind.PauseActivity;

        builder.AddReason(
            "memory:repeated-failures",
            $"Repeated failure pattern detected ({memoryContext.OutcomeSummary.FailureCount} failures). Escalating safeguard.");
        builder.AddDirective(
            directiveKind,
            shouldAbort ? boostedAbortPriority : boostedPausePriority,
            targetId: null,
            targetLabel: context.SessionDomainState.Location.ContextLabel,
            suggestedPolicy: directiveKind.ToString(),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["memoryInfluenced"] = "true",
                ["memoryFailureCount"] = memoryContext.OutcomeSummary.FailureCount.ToString(),
                ["memoryEscalationBoost"] = abortOptions.MemoryReinforceAbortPriorityBoost.ToString()
            },
            blocks: true,
            aborts: shouldAbort);

        builder.AddMemoryInfluence(
            MemoryInfluenceHelpers.CreateInfluenceTrace(
                Name,
                shouldAbort ? "AbortEscalation" : "PauseEscalation",
                "outcome:failure-pattern",
                "memory:repeated-failures",
                "Repeated failures reinforced abort/pause policy output.",
                memoryContext.OutcomeSummary.FailureCount.ToString()));
    }
}
