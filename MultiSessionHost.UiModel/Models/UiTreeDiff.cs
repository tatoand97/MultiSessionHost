namespace MultiSessionHost.UiModel.Models;

public sealed record UiTreeDiff(
    IReadOnlyList<string> AddedNodeIds,
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<string> ChangedNodeIds)
{
    public bool HasChanges =>
        AddedNodeIds.Count > 0 ||
        RemovedNodeIds.Count > 0 ||
        ChangedNodeIds.Count > 0;
}
