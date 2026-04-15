namespace MultiSessionHost.Contracts.Sessions;

public sealed record UiCommandRequest(
    string? NodeId,
    string Kind,
    string? ActionName,
    string? TextValue,
    bool? BoolValue,
    string? SelectedValue,
    IReadOnlyDictionary<string, string?>? Metadata);
