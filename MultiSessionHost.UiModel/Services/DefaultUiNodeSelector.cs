using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Services;

public sealed class DefaultUiNodeSelector : IUiNodeSelector
{
    public UiNode? FindFirstByRole(UiTree tree, string role) => tree.FindByRole(role);

    public UiNode? FindFirstByExactText(UiTree tree, string text) => tree.FindByExactText(text);

    public UiNode? FindFirst(UiTree tree, Func<UiNode, bool> predicate) => tree.FindByPredicate(predicate);

    public IReadOnlyList<UiNode> Flatten(UiTree tree) => tree.Flatten();
}
