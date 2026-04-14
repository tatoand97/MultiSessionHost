namespace MultiSessionHost.Core.Models;

public sealed record ProcessHealthSnapshot(
    DateTimeOffset GeneratedAtUtc,
    int ActiveSessions,
    int FaultedSessions,
    long TotalTicksExecuted,
    long TotalErrors,
    long TotalRetries,
    long TotalHeartbeatsEmitted,
    IReadOnlyCollection<SessionHealthSnapshot> Sessions);
