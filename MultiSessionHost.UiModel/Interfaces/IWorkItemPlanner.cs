using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Interfaces;

public interface IWorkItemPlanner
{
    IReadOnlyList<PlannedUiWorkItem> Plan(UiTree tree);
}
