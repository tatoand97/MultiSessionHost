using MultiSessionHost.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MultiSessionHost.Desktop.Policy;

public sealed class TransitPolicy : IPolicy
{
    private readonly SessionHostOptions _options;
    private readonly IPolicyRuleProvider _ruleProvider;
    private readonly IPolicyRuleMatcher _matcher;

    public TransitPolicy(SessionHostOptions options)
        : this(options, new ConfiguredPolicyRuleProvider(options), new DefaultPolicyRuleMatcher())
    {
    }

    [ActivatorUtilitiesConstructor]
    public TransitPolicy(SessionHostOptions options, IPolicyRuleProvider ruleProvider, IPolicyRuleMatcher matcher)
    {
        _options = options;
        _ruleProvider = ruleProvider;
        _matcher = matcher;
    }

    public string Name => nameof(TransitPolicy);

    public ValueTask<PolicyEvaluationResult> EvaluateAsync(PolicyEvaluationContext context, CancellationToken cancellationToken)
    {
        var builder = new PolicyResultBuilder(Name);
        var candidate = PolicyCandidateFactory.CreateTransit(context);
        var candidates = new PolicyRuleCandidate[] { candidate };
        builder.SetCandidateSummary(PolicyRuleEvaluation.CandidateSummary(candidates));
        var rules = _ruleProvider.GetRules();

        _ = PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.TransitRules, candidates, context.Now, static _ => null) ||
            PolicyRuleEvaluation.TryApplyFirst(builder, _matcher, rules.TransitFallbackRules, candidates, context.Now, static _ => null);

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

        var transitOptions = memoryOptions.Transit;
        if (!MemoryInfluenceHelpers.HasRepeatedLongWaitPattern(memoryContext.TimingSummary, transitOptions))
        {
            return;
        }

        var shouldMoveOn = transitOptions.AdaptToRememberedDelays &&
            memoryContext.TimingSummary.AverageWaitWindowMs >= transitOptions.MaxRememberedWaitBeforeMoveOnMs;

        if (shouldMoveOn)
        {
            builder.AddReason(
                "memory:move-on-after-long-waits",
                $"Remembered wait pattern is above threshold ({memoryContext.TimingSummary.AverageWaitWindowMs:0}ms). Moving on.");
            builder.AddDirective(
                DecisionDirectiveKind.Navigate,
                _options.PolicyEngine.TransitPolicy.NavigatePriority,
                targetId: null,
                targetLabel: context.SessionDomainState.Location.ContextLabel,
                suggestedPolicy: "Navigate",
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["memoryInfluenced"] = "true",
                    ["memoryAverageWaitMs"] = memoryContext.TimingSummary.AverageWaitWindowMs.ToString("0")
                },
                blocks: false,
                aborts: false);

            builder.AddMemoryInfluence(
                MemoryInfluenceHelpers.CreateInfluenceTrace(
                    Name,
                    "TransitMoveOn",
                    "timing:wait-window",
                    "memory:move-on-after-long-waits",
                    "Historical wait windows exceed configured move-on threshold.",
                    memoryContext.TimingSummary.AverageWaitWindowMs.ToString("0")));
            return;
        }

        builder.AddReason(
            "memory:wait-due-to-long-waits",
            $"Remembered long-wait pattern detected ({memoryContext.TimingSummary.AverageWaitWindowMs:0}ms avg). Waiting.");
        builder.AddDirective(
            DecisionDirectiveKind.Wait,
            _options.PolicyEngine.TransitPolicy.WaitPriority,
            targetId: null,
            targetLabel: context.SessionDomainState.Location.ContextLabel,
            suggestedPolicy: "Wait",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["memoryInfluenced"] = "true",
                ["memoryAverageWaitMs"] = memoryContext.TimingSummary.AverageWaitWindowMs.ToString("0")
            },
            blocks: true,
            aborts: false);

        builder.AddMemoryInfluence(
            MemoryInfluenceHelpers.CreateInfluenceTrace(
                Name,
                "TransitWait",
                "timing:wait-window",
                "memory:wait-due-to-long-waits",
                "Historical wait windows indicate repeated long waits.",
                memoryContext.TimingSummary.AverageWaitWindowMs.ToString("0")));
    }
}
