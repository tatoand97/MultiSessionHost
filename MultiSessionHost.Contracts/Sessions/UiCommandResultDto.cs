namespace MultiSessionHost.Contracts.Sessions;

public sealed record UiCommandResultDto(
    bool Succeeded,
    string SessionId,
    string? NodeId,
    string Kind,
    string Message,
    DateTimeOffset ExecutedAtUtc,
    bool UpdatedUiStateAvailable,
    string? FailureCode);
