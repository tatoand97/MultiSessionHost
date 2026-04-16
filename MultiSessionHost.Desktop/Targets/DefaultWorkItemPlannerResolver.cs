using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Interfaces;

namespace MultiSessionHost.Desktop.Targets;

public sealed class DefaultWorkItemPlannerResolver : IWorkItemPlannerResolver
{
    private readonly DefaultButtonWorkItemPlanner _defaultButtonWorkItemPlanner;
    private readonly TestAppWorkItemPlanner _testAppWorkItemPlanner;

    public DefaultWorkItemPlannerResolver(
        DefaultButtonWorkItemPlanner defaultButtonWorkItemPlanner,
        TestAppWorkItemPlanner testAppWorkItemPlanner)
    {
        _defaultButtonWorkItemPlanner = defaultButtonWorkItemPlanner;
        _testAppWorkItemPlanner = testAppWorkItemPlanner;
    }

    public IWorkItemPlanner Resolve(ResolvedDesktopTargetContext context) =>
        context.Profile.Kind switch
        {
            DesktopTargetKind.SelfHostedHttpDesktop => _defaultButtonWorkItemPlanner,
            DesktopTargetKind.DesktopTestApp => _testAppWorkItemPlanner,
            DesktopTargetKind.WindowsUiAutomationDesktop => _defaultButtonWorkItemPlanner,
            _ => throw new InvalidOperationException($"Desktop target kind '{context.Profile.Kind}' is not supported.")
        };
}
