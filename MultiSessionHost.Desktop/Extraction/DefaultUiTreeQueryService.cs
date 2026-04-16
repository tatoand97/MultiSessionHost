using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class DefaultUiTreeQueryService : IUiTreeQueryService
{
    public IReadOnlyList<UiNode> Flatten(UiTree tree)
    {
        var nodes = new List<UiNode>();

        if (tree?.Root is null)
        {
            return nodes;
        }

        Visit(tree.Root, nodes);
        return nodes;
    }

    public IReadOnlyList<UiNode> EnumerateVisibleDescendants(UiNode node) =>
        GetDescendants(node).Where(static descendant => descendant is not null && descendant.Visible).ToArray();

    public IReadOnlyList<UiNode> FindByRole(UiTree tree, string role) =>
        Flatten(tree)
            .Where(node => string.Equals(node?.Role ?? string.Empty, role, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public IReadOnlyList<UiNode> FindByTextFragment(UiTree tree, string fragment) =>
        Flatten(tree)
            .Where(node => GatherTextCandidates(node).Any(text => text.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    public IReadOnlyList<UiNode> FindByAttribute(UiTree tree, string attributeName, string? value = null) =>
        Flatten(tree)
            .Where(node =>
            {
                var attributeValue = GetAttribute(node, attributeName);
                return attributeValue is not null &&
                       (value is null || string.Equals(attributeValue, value, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

    public UiNode? FindParent(UiTree tree, UiNode node) =>
        tree?.Root is null || node is null ? null : FindParent(tree.Root, node.Id);

    public IReadOnlyList<UiNode> GetAncestors(UiTree tree, UiNode node)
    {
        var ancestors = new List<UiNode>();

        if (tree?.Root is null || node is null)
        {
            return ancestors;
        }

        var current = node;

        while (FindParent(tree, current) is { } parent)
        {
            ancestors.Add(parent);
            current = parent;
        }

        ancestors.Reverse();
        return ancestors;
    }

    public IReadOnlyList<UiNode> GetDescendants(UiNode node)
    {
        var nodes = new List<UiNode>();

        if (node is null)
        {
            return nodes;
        }

        foreach (var child in node.Children ?? Array.Empty<UiNode>())
        {
            if (child is not null)
            {
                Visit(child, nodes);
            }
        }

        return nodes;
    }

    public IReadOnlyList<UiNode> GetSiblings(UiTree tree, UiNode node)
    {
        var parent = FindParent(tree, node);
        return parent is null
            ? []
            : (parent.Children ?? Array.Empty<UiNode>()).Where(child => child is not null && child.Id != node.Id).ToArray();
    }

    public IReadOnlyList<string> ComputeNodePath(UiTree tree, UiNode node) =>
        GetAncestors(tree, node)
            .Concat(node is null ? [] : [node])
            .Select(pathNode => $"{pathNode.Role}:{pathNode.Id.Value}")
            .ToArray();

    public IReadOnlyList<string> GatherTextCandidates(UiNode node)
    {
        var values = new List<string>();

        if (node is null)
        {
            return values;
        }

        AddIfNotBlank(values, node.Name);
        AddIfNotBlank(values, node.Text);

        foreach (var attribute in node.Attributes ?? Array.Empty<UiAttribute>())
        {
            if (attribute is not null && IsTextLikeAttribute(attribute.Name ?? string.Empty))
            {
                AddIfNotBlank(values, attribute.Value);
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlySet<string> DeriveRoleFamilies(UiNode node)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var role = node?.Role ?? string.Empty;

        if (ContainsAny(role, "list", "grid", "table", "combo", "tree"))
            families.Add("list");

        if (ContainsAny(role, "button", "checkbox", "toggle", "menuitem", "link"))
            families.Add("action");

        if (ContainsAny(role, "progress", "status", "spinner"))
            families.Add("status");

        if (ContainsAny(role, "label", "text", "textbox"))
            families.Add("text");

        if (ContainsAny(role, "panel", "group", "form", "window"))
            families.Add("container");

        return families;
    }

    public string? GetAttribute(UiNode node, string attributeName)
    {
        if (node is null || string.IsNullOrWhiteSpace(attributeName))
        {
            return null;
        }

        var attributes = node.Attributes;
        if (attributes is null || attributes.Count == 0)
        {
            return null;
        }

        return attributes
            .FirstOrDefault(attribute =>
                attribute is not null &&
                string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static void Visit(UiNode node, ICollection<UiNode> nodes)
    {
        if (node is null)
        {
            return;
        }

        nodes.Add(node);

        foreach (var child in node.Children ?? Array.Empty<UiNode>())
        {
            if (child is not null)
            {
                Visit(child, nodes);
            }
        }
    }

    private static UiNode? FindParent(UiNode current, UiNodeId childId)
    {
        if (current is null)
        {
            return null;
        }

        var children = current.Children ?? Array.Empty<UiNode>();

        if (children.Any(child => child is not null && child.Id == childId))
        {
            return current;
        }

        foreach (var child in children)
        {
            if (child is null)
            {
                continue;
            }

            var parent = FindParent(child, childId);
            if (parent is not null)
            {
                return parent;
            }
        }

        return null;
    }

    private static bool IsTextLikeAttribute(string name) =>
        ContainsAny(name ?? string.Empty, "label", "title", "text", "name", "value", "status", "message", "selectedItem", "items", "semanticRole");

    private static void AddIfNotBlank(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => (value ?? string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase));
}