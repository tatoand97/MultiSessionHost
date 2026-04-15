using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public class DefaultButtonWorkItemPlanner : IWorkItemPlanner
{
    public IReadOnlyList<PlannedUiWorkItem> Plan(UiTree tree) =>
        tree
            .Flatten()
            .Where(static node => string.Equals(node.Role, "Button", StringComparison.OrdinalIgnoreCase))
            .Where(static node => node.Visible && node.Enabled && !string.IsNullOrWhiteSpace(node.Text))
            .Select(node => new PlannedUiWorkItem("InvokeButton", $"Button '{node.Text}' is available.", node.Id.Value))
            .ToArray();
}
