using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Interfaces;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IWorkItemPlannerResolver
{
    IWorkItemPlanner Resolve(ResolvedDesktopTargetContext context);
}
