namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopWindowInfo(
    long WindowHandle,
    int ProcessId,
    string Title,
    bool IsVisible);
