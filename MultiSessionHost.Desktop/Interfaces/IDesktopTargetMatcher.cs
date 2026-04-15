using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetMatcher
{
    (DesktopProcessInfo Process, DesktopWindowInfo Window) Match(
        IReadOnlyList<DesktopProcessInfo> processes,
        IReadOnlyList<DesktopWindowInfo> windows,
        DesktopSessionTarget target);
}
