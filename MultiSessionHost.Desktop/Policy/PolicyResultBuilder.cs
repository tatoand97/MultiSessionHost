namespace MultiSessionHost.Desktop.Policy;

internal sealed class PolicyResultBuilder
{
    private readonly List<DecisionDirective> _directives = [];
    private readonly List<DecisionReason> _reasons = [];
    private readonly List<string> _warnings = [];

    public PolicyResultBuilder(string policyName)
    {
        PolicyName = policyName;
    }

    public string PolicyName { get; }

    public bool DidBlock { get; private set; }

    public bool DidAbort { get; private set; }

    public void AddReason(string code, string message, IReadOnlyDictionary<string, string>? metadata = null) =>
        _reasons.Add(new DecisionReason(PolicyName, code, message, metadata ?? new Dictionary<string, string>()));

    public void AddWarning(string warning) => _warnings.Add(warning);

    public void AddDirective(
        DecisionDirectiveKind kind,
        int priority,
        string? targetId,
        string? targetLabel,
        string? suggestedPolicy,
        IReadOnlyDictionary<string, string>? metadata = null,
        bool blocks = false,
        bool aborts = false)
    {
        if (blocks)
        {
            DidBlock = true;
        }

        if (aborts)
        {
            DidAbort = true;
            DidBlock = true;
        }

        var reasons = _reasons.ToArray();
        var directive = new DecisionDirective(
            DirectiveId: CreateDirectiveId(PolicyName, kind, targetId, targetLabel),
            kind,
            priority,
            PolicyName,
            targetId,
            targetLabel,
            suggestedPolicy,
            metadata ?? new Dictionary<string, string>(),
            reasons);
        _directives.Add(directive);
    }

    public PolicyEvaluationResult Build() =>
        new(
            PolicyName,
            _directives.ToArray(),
            _reasons.ToArray(),
            _warnings.ToArray(),
            DidMatch: _directives.Count > 0 || _reasons.Count > 0 || _warnings.Count > 0,
            DidBlock,
            DidAbort);

    private static string CreateDirectiveId(string policyName, DecisionDirectiveKind kind, string? targetId, string? targetLabel)
    {
        var target = string.IsNullOrWhiteSpace(targetId)
            ? targetLabel ?? "global"
            : targetId;

        return string.Join(
                ":",
                policyName,
                kind,
                target)
            .ToLowerInvariant()
            .Replace(' ', '-');
    }
}
