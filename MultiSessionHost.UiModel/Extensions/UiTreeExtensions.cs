using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Extensions;

public static class UiTreeExtensions
{
    public static IReadOnlyList<UiNode> Flatten(this UiTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var nodes = new List<UiNode>();
        Traverse(tree.Root, nodes);
        return nodes;
    }

    public static UiNode? FindByRole(this UiTree tree, string role)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return tree.Flatten().FirstOrDefault(node => string.Equals(node.Role, role, StringComparison.OrdinalIgnoreCase));
    }

    public static UiNode? FindByExactText(this UiTree tree, string text)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return tree.Flatten().FirstOrDefault(node => string.Equals(node.Text, text, StringComparison.Ordinal));
    }

    public static UiNode? FindByPredicate(this UiTree tree, Func<UiNode, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(predicate);

        return tree.Flatten().FirstOrDefault(predicate);
    }

    private static void Traverse(UiNode node, ICollection<UiNode> nodes)
    {
        nodes.Add(node);

        foreach (var child in node.Children)
        {
            Traverse(child, nodes);
        }
    }
}
