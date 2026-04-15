namespace MultiSessionHost.Contracts.Sessions;

public sealed record NodeInvokeCommandRequest(
    string? ActionName,
    IReadOnlyDictionary<string, string?>? Metadata);
