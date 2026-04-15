namespace MultiSessionHost.Contracts.Sessions;

public sealed record DesktopTargetProfileOverrideDto(
    string? ProcessName,
    string? WindowTitleFragment,
    string? CommandLineFragmentTemplate,
    string? BaseAddressTemplate,
    string? MatchingMode,
    IReadOnlyDictionary<string, string?> Metadata,
    bool? SupportsUiSnapshots,
    bool? SupportsStateEndpoint);
