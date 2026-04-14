namespace MultiSessionHost.Contracts.Sessions;

public sealed record ProcessHealthDto(
    DateTimeOffset GeneratedAtUtc,
    int ActiveSessions,
    int FaultedSessions,
    long TotalTicksExecuted,
    long TotalErrors,
    long TotalRetries,
    long TotalHeartbeatsEmitted,
    IReadOnlyCollection<SessionHealthDto> Sessions);
