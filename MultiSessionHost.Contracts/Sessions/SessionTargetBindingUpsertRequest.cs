namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionTargetBindingUpsertRequest(
    string TargetProfileName,
    IReadOnlyDictionary<string, string> Variables,
    DesktopTargetProfileOverrideDto? Overrides);
