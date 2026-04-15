using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public interface IUiTreeQueryService
{
    IReadOnlyList<UiNode> Flatten(UiTree tree);

    IReadOnlyList<UiNode> EnumerateVisibleDescendants(UiNode node);

    IReadOnlyList<UiNode> FindByRole(UiTree tree, string role);

    IReadOnlyList<UiNode> FindByTextFragment(UiTree tree, string fragment);

    IReadOnlyList<UiNode> FindByAttribute(UiTree tree, string attributeName, string? value = null);

    UiNode? FindParent(UiTree tree, UiNode node);

    IReadOnlyList<UiNode> GetAncestors(UiTree tree, UiNode node);

    IReadOnlyList<UiNode> GetDescendants(UiNode node);

    IReadOnlyList<UiNode> GetSiblings(UiTree tree, UiNode node);

    IReadOnlyList<string> ComputeNodePath(UiTree tree, UiNode node);

    IReadOnlyList<string> GatherTextCandidates(UiNode node);

    IReadOnlySet<string> DeriveRoleFamilies(UiNode node);

    string? GetAttribute(UiNode node, string attributeName);
}
