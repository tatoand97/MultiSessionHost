using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Services;

public sealed class DefaultUiStateProjector : IUiStateProjector
{
    public UiTreeDiff Project(UiTree? previousTree, UiTree currentTree)
    {
        ArgumentNullException.ThrowIfNull(currentTree);

        if (previousTree is null)
        {
            var added = currentTree.Flatten().Select(static node => node.Id.Value).ToArray();
            return new UiTreeDiff(added, [], []);
        }

        var previous = previousTree.Flatten().ToDictionary(static node => node.Id.Value, StringComparer.Ordinal);
        var current = currentTree.Flatten().ToDictionary(static node => node.Id.Value, StringComparer.Ordinal);

        var addedNodeIds = current.Keys.Except(previous.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var removedNodeIds = previous.Keys.Except(current.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var changedNodeIds = current
            .Where(entry => previous.TryGetValue(entry.Key, out var previousNode) && !Equals(previousNode, entry.Value))
            .Select(static entry => entry.Key)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        return new UiTreeDiff(addedNodeIds, removedNodeIds, changedNodeIds);
    }
}
