using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopSessionAttachment(
    SessionId SessionId,
    DesktopSessionTarget Target,
    DesktopProcessInfo Process,
    DesktopWindowInfo Window,
    Uri BaseAddress,
    DateTimeOffset AttachedAtUtc);
