using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopSessionTarget(
    SessionId SessionId,
    string ProcessName,
    string WindowTitleFragment,
    string CommandLineFragment,
    int ExpectedPort,
    Uri BaseAddress);
