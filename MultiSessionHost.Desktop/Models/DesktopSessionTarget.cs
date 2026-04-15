using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopSessionTarget(
    SessionId SessionId,
    string ProfileName,
    DesktopTargetKind Kind,
    DesktopSessionMatchingMode MatchingMode,
    string ProcessName,
    string? WindowTitleFragment,
    string? CommandLineFragment,
    Uri? BaseAddress,
    IReadOnlyDictionary<string, string?> Metadata);
