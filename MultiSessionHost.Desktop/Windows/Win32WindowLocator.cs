using System.Text;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Native;

namespace MultiSessionHost.Desktop.Windows;

public sealed class Win32WindowLocator : IWindowLocator
{
    public IReadOnlyCollection<DesktopWindowInfo> GetWindows()
    {
        var windows = new List<DesktopWindowInfo>();

        Win32NativeMethods.EnumWindows(
            (windowHandle, _) =>
            {
                var title = GetWindowTitle(windowHandle);
                var isVisible = Win32NativeMethods.IsWindowVisible(windowHandle);
                Win32NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
                windows.Add(new DesktopWindowInfo(windowHandle.ToInt64(), (int)processId, title, isVisible));
                return true;
            },
            0);

        return windows;
    }

    public DesktopWindowInfo? GetWindowByHandle(long handle) =>
        GetWindows().FirstOrDefault(window => window.WindowHandle == handle);

    private static string GetWindowTitle(nint windowHandle)
    {
        var length = Win32NativeMethods.GetWindowTextLengthW(windowHandle);

        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        Win32NativeMethods.GetWindowTextW(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }
}
