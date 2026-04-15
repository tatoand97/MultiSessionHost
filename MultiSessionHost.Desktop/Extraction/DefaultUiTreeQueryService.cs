using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class DefaultUiTreeQueryService : IUiTreeQueryService
{
    public IReadOnlyList<UiNode> Flatten(UiTree tree)
    {
        var nodes = new List<UiNode>();
        Visit(tree.Root, nodes);
        return nodes;
    }

    public IReadOnlyList<UiNode> EnumerateVisibleDescendants(UiNode node) =>
        GetDescendants(node).Where(static descendant => descendant.Visible).ToArray();

    public IReadOnlyList<UiNode> FindByRole(UiTree tree, string role) =>
        Flatten(tree)
            .Where(node => string.Equals(node.Role, role, StringComparison.OrdinalIgnoreCase))
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
        FindParent(tree.Root, node.Id);

    public IReadOnlyList<UiNode> GetAncestors(UiTree tree, UiNode node)
    {
        var ancestors = new List<UiNode>();
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

        foreach (var child in node.Children)
        {
            Visit(child, nodes);
        }

        return nodes;
    }

    public IReadOnlyList<UiNode> GetSiblings(UiTree tree, UiNode node)
    {
        var parent = FindParent(tree, node);
        return parent is null ? [] : parent.Children.Where(child => child.Id != node.Id).ToArray();
    }

    public IReadOnlyList<string> ComputeNodePath(UiTree tree, UiNode node) =>
        GetAncestors(tree, node)
            .Concat([node])
            .Select(static pathNode => $"{pathNode.Role}:{pathNode.Id.Value}")
            .ToArray();

    public IReadOnlyList<string> GatherTextCandidates(UiNode node)
    {
        var values = new List<string>();
        AddIfNotBlank(values, node.Name);
        AddIfNotBlank(values, node.Text);

        foreach (var attribute in node.Attributes)
        {
            if (IsTextLikeAttribute(attribute.Name))
            {
                AddIfNotBlank(values, attribute.Value);
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlySet<string> DeriveRoleFamilies(UiNode node)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var role = node.Role;

        if (ContainsAny(role, "list", "grid", "table", "combo", "tree"))
        {
            families.Add("list");
        }

        if (ContainsAny(role, "button", "checkbox", "toggle", "menuitem", "link"))
        {
            families.Add("action");
        }

        if (ContainsAny(role, "progress", "status", "spinner"))
        {
            families.Add("status");
        }

        if (ContainsAny(role, "label", "text", "textbox"))
        {
            families.Add("text");
        }

        if (ContainsAny(role, "panel", "group", "form", "window"))
        {
            families.Add("container");
        }

        return families;
    }

    public string? GetAttribute(UiNode node, string attributeName) =>
        node.Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static void Visit(UiNode node, ICollection<UiNode> nodes)
    {
        nodes.Add(node);

        foreach (var child in node.Children)
        {
            Visit(child, nodes);
        }
    }

    private static UiNode? FindParent(UiNode current, UiNodeId childId)
    {
        if (current.Children.Any(child => child.Id == childId))
        {
            return current;
        }

        foreach (var child in current.Children)
        {
            var parent = FindParent(child, childId);

            if (parent is not null)
            {
                return parent;
            }
        }

        return null;
    }

    private static bool IsTextLikeAttribute(string name) =>
        ContainsAny(name, "label", "title", "text", "name", "value", "status", "message", "selectedItem", "items", "semanticRole");

    private static void AddIfNotBlank(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
