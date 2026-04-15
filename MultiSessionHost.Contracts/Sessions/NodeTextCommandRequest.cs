namespace MultiSessionHost.Contracts.Sessions;

public sealed record NodeTextCommandRequest(
    string? TextValue,
    IReadOnlyDictionary<string, string?>? Metadata);
