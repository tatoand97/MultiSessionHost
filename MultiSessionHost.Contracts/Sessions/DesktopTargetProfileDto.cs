namespace MultiSessionHost.Contracts.Sessions;

public sealed record DesktopTargetProfileDto(
    string ProfileName,
    string Kind,
    string ProcessName,
    string? WindowTitleFragment,
    string? CommandLineFragmentTemplate,
    string? BaseAddressTemplate,
    string MatchingMode,
    IReadOnlyDictionary<string, string?> Metadata,
    bool SupportsUiSnapshots,
    bool SupportsStateEndpoint);
