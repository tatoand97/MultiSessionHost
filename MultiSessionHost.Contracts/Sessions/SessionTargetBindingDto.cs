namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionTargetBindingDto(
    string SessionId,
    string TargetProfileName,
    IReadOnlyDictionary<string, string> Variables,
    DesktopTargetProfileOverrideDto? Overrides);
