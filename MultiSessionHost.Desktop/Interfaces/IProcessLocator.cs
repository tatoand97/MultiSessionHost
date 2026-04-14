using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IProcessLocator
{
    IReadOnlyCollection<DesktopProcessInfo> GetProcesses(string? processName = null);

    DesktopProcessInfo? GetProcessById(int processId);
}
