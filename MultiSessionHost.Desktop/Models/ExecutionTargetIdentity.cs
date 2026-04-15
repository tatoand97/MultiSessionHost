using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Models;

public sealed record ExecutionTargetIdentity(
    DesktopTargetKind Kind,
    string ProfileName,
    string ProcessName,
    string? BaseAddress,
    string? WindowTitleFragment,
    string? CommandLineFragment,
    string CanonicalKey);
