using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IWindowLocator
{
    IReadOnlyCollection<DesktopWindowInfo> GetWindows();

    DesktopWindowInfo? GetWindowByHandle(long handle);
}
