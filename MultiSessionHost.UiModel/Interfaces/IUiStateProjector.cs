using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Interfaces;

public interface IUiStateProjector
{
    UiTreeDiff Project(UiTree? previousTree, UiTree currentTree);
}
