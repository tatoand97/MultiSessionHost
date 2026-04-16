using System.Security.Cryptography;
using System.Text;

namespace MultiSessionHost.Desktop.Automation;

public sealed class NativeUiAutomationIdentityBuilder
{
    public NativeUiAutomationNode AssignIdentities(NativeUiAutomationElementSnapshot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Assign(root, "root", 0, []);
    }

    private NativeUiAutomationNode Assign(
        NativeUiAutomationElementSnapshot element,
        string parentSignature,
        int siblingIndex,
        IReadOnlyList<string> ancestors)
    {
        var identity = BuildIdentity(element, parentSignature, siblingIndex, ancestors);
        var childOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var childAncestors = ancestors.Concat([identity.NodeId]).ToArray();
        var children = new List<NativeUiAutomationNode>(element.Children.Count);

        foreach (var child in element.Children)
        {
            var childKey = SemanticKey(child);
            childOccurrences.TryGetValue(childKey, out var occurrence);
            childOccurrences[childKey] = occurrence + 1;
            children.Add(Assign(child, identity.NodeId, occurrence, childAncestors));
        }

        return new NativeUiAutomationNode(
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
            children);
    }

    public NativeUiNodeIdentity BuildIdentity(
        NativeUiAutomationElementSnapshot element,
        string parentSignature,
        int siblingIndex,
        IReadOnlyList<string> ancestors)
    {
        if (!string.IsNullOrWhiteSpace(element.AutomationId))
        {
            return new NativeUiNodeIdentity(
                $"uia:{Hash("automation-id", parentSignature, element.FrameworkId, element.Role, element.AutomationId)}",
                "Strong",
                "automation-id+ancestor");
        }

        if (!string.IsNullOrWhiteSpace(element.RuntimeId))
        {
            return new NativeUiNodeIdentity(
                $"uia:{Hash("runtime-id", parentSignature, element.Role, element.RuntimeId)}",
                "Strong",
                "runtime-id+ancestor");
        }

        var semantic = SemanticKey(element);

        if (!string.IsNullOrWhiteSpace(element.Name) || !string.IsNullOrWhiteSpace(element.ClassName))
        {
            return new NativeUiNodeIdentity(
                $"uia:{Hash("semantic", parentSignature, semantic, siblingIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
                "Composite",
                "semantic-attributes+ancestor+occurrence");
        }

        return new NativeUiNodeIdentity(
            $"uia:{Hash("structural", string.Join("/", ancestors), element.Role, siblingIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
            "Fallback",
            "ancestor-path+role+occurrence");
    }

    public string SemanticKey(NativeUiAutomationElementSnapshot element) =>
        string.Join(
            "|",
            Normalize(element.FrameworkId),
            Normalize(element.Role),
            Normalize(element.ClassName),
            Normalize(element.Name),
            Normalize(element.Value));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string Hash(params string?[] parts)
    {
        var input = string.Join('\u001f', parts.Select(static part => part ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }
}
