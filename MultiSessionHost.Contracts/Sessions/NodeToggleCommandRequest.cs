namespace MultiSessionHost.Contracts.Sessions;

public sealed record NodeToggleCommandRequest(
    bool? BoolValue,
    IReadOnlyDictionary<string, string?>? Metadata);
