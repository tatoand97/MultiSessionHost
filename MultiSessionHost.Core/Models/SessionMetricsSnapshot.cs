namespace MultiSessionHost.Core.Models;

public sealed record SessionMetricsSnapshot(
    long TicksExecuted,
    long Errors,
    long Retries,
    long HeartbeatsEmitted);
