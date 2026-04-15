namespace MultiSessionHost.Contracts.Sessions;

public sealed record ResolvedDesktopTargetDto(
    string SessionId,
    string ProfileName,
    string Kind,
    string MatchingMode,
    string ProcessName,
    string? WindowTitleFragment,
    string? CommandLineFragment,
    string? BaseAddress,
    IReadOnlyDictionary<string, string?> Metadata);
