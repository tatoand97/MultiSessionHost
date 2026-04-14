namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionMetricsDto(
    long TicksExecuted,
    long Errors,
    long Retries,
    long HeartbeatsEmitted);
