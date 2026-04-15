using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Models;

public sealed record DesktopTargetProfileOverride(
    string? ProcessName,
    string? WindowTitleFragment,
    string? CommandLineFragmentTemplate,
    string? BaseAddressTemplate,
    DesktopSessionMatchingMode? MatchingMode,
    IReadOnlyDictionary<string, string?> Metadata,
    bool? SupportsUiSnapshots,
    bool? SupportsStateEndpoint);
