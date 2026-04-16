using MultiSessionHost.Core.Constants;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Automation;

public sealed class NativeUiAutomationElementLocator : INativeUiAutomationElementLocator
{
    private readonly INativeUiAutomationElementProvider _elementProvider;
    private readonly NativeUiAutomationIdentityBuilder _identityBuilder;

    public NativeUiAutomationElementLocator(
        INativeUiAutomationElementProvider elementProvider,
        NativeUiAutomationIdentityBuilder identityBuilder)
    {
        _elementProvider = elementProvider;
        _identityBuilder = identityBuilder;
    }

    public Task<LocatedNativeUiElement> LocateAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(action);

        cancellationToken.ThrowIfCancellationRequested();
        var root = _elementProvider.GetRoot(attachment);
        var options = NativeUiAutomationCaptureOptions.FromMetadata(context.Target.Metadata);
        var candidates = new List<Candidate>();
        var tree = Capture(root, options, "root", siblingIndex: 0, ancestors: [], depth: 0, candidates, cancellationToken);
        var nodeId = action.Node.Id.Value;
        var exact = candidates.FirstOrDefault(candidate => string.Equals(candidate.Node.NodeId, nodeId, StringComparison.Ordinal));

        if (exact is not null)
        {
            return Task.FromResult(new LocatedNativeUiElement(exact.Element, exact.Node, "node-id", IsExactNodeIdMatch: true));
        }

        var fuzzy = candidates
            .Select(candidate => new { Candidate = candidate, Score = Score(candidate.Node, action) })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate.Node.IdentityQuality, StringComparer.Ordinal)
            .FirstOrDefault();

        if (fuzzy is not null && fuzzy.Score >= RequiredScore(action))
        {
            return Task.FromResult(new LocatedNativeUiElement(fuzzy.Candidate.Element, fuzzy.Candidate.Node, "identity-metadata", IsExactNodeIdMatch: false));
        }

        var identityBasis = GetMetadata(action, "identityBasis");
        var message = string.Equals(identityBasis, "runtime-id+ancestor", StringComparison.OrdinalIgnoreCase)
            ? $"Native UIA node '{nodeId}' appears stale and could not be re-located from the live target."
            : $"Native UIA node '{nodeId}' could not be found in the live target.";
        var code = string.Equals(identityBasis, "runtime-id+ancestor", StringComparison.OrdinalIgnoreCase)
            ? UiCommandFailureCodes.NativeElementStale
            : UiCommandFailureCodes.NativeElementNotFound;

        _ = tree;
        throw new NativeUiAutomationInteractionException(code, message);
    }

    private Candidate Capture(
        INativeUiAutomationElement element,
        NativeUiAutomationCaptureOptions options,
        string parentSignature,
        int siblingIndex,
        IReadOnlyList<string> ancestors,
        int depth,
        ICollection<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ToSnapshot(element, children: []);
        var identity = _identityBuilder.BuildIdentity(snapshot, parentSignature, siblingIndex, ancestors);
        var childAncestors = ancestors.Concat([identity.NodeId]).ToArray();
        var childCandidates = new List<Candidate>();

        if (depth < options.MaxDepth)
        {
            var childOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var child in element.GetChildren(options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childSnapshot = ToSnapshot(child, children: []);
                var childKey = _identityBuilder.SemanticKey(childSnapshot);
                childOccurrences.TryGetValue(childKey, out var occurrence);
                childOccurrences[childKey] = occurrence + 1;
                childCandidates.Add(Capture(child, options, identity.NodeId, occurrence, childAncestors, depth + 1, candidates, cancellationToken));
            }
        }

        var node = new NativeUiAutomationNode(
            identity.NodeId,
            identity.Quality,
            identity.Basis,
            element.Role,
            element.Name,
            element.AutomationId,
            element.RuntimeId,
            element.FrameworkId,
            element.ClassName,
            element.IsEnabled,
            element.IsOffscreen,
            element.HasKeyboardFocus,
            element.IsSelected,
            element.Value,
            element.Bounds,
            element.Metadata,
            childCandidates.Select(static candidate => candidate.Node).ToArray());
        var candidate = new Candidate(element, node);
        candidates.Add(candidate);
        return candidate;
    }

    private static NativeUiAutomationElementSnapshot ToSnapshot(
        INativeUiAutomationElement element,
        IReadOnlyList<NativeUiAutomationElementSnapshot> children) =>
        new(
            element.Role,
            element.Name,
            element.AutomationId,
            element.RuntimeId,
            element.FrameworkId,
            element.ClassName,
            element.IsEnabled,
            element.IsOffscreen,
            element.HasKeyboardFocus,
            element.IsSelected,
            element.Value,
            element.Bounds,
            element.Metadata,
            children);

    private static int Score(NativeUiAutomationNode node, ResolvedUiAction action)
    {
        var score = 0;

        if (Matches(node.RuntimeId, GetMetadata(action, "runtimeId")))
        {
            score += 100;
        }

        if (Matches(node.AutomationId, GetMetadata(action, "automationId")))
        {
            score += 80;
        }

        if (Matches(node.FrameworkId, GetMetadata(action, "frameworkId")))
        {
            score += 20;
        }

        if (Matches(node.ClassName, GetMetadata(action, "className")))
        {
            score += 20;
        }

        if (Matches(node.Role, action.Node.Role))
        {
            score += 20;
        }

        if (Matches(node.Name, action.Node.Name))
        {
            score += 20;
        }

        return score;
    }

    private static int RequiredScore(ResolvedUiAction action) =>
        string.IsNullOrWhiteSpace(GetMetadata(action, "automationId")) && string.IsNullOrWhiteSpace(GetMetadata(action, "runtimeId"))
            ? 60
            : 100;

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? GetMetadata(ResolvedUiAction action, string key) =>
        action.Metadata.TryGetValue(key, out var value) ? value : null;

    private sealed record Candidate(INativeUiAutomationElement Element, NativeUiAutomationNode Node);
}
