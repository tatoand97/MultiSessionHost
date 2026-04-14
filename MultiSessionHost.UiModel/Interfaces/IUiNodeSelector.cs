using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Interfaces;

public interface IUiNodeSelector
{
    UiNode? FindFirstByRole(UiTree tree, string role);

    UiNode? FindFirstByExactText(UiTree tree, string text);

    UiNode? FindFirst(UiTree tree, Func<UiNode, bool> predicate);

    IReadOnlyList<UiNode> Flatten(UiTree tree);
}
