namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopProcessInfo(
    int ProcessId,
    string ProcessName,
    string? CommandLine,
    long MainWindowHandle);
