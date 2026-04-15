namespace MultiSessionHost.Contracts.Sessions;

public sealed record NodeSelectCommandRequest(
    string? SelectedValue,
    IReadOnlyDictionary<string, string?>? Metadata);
