namespace MultiSessionHost.Core.Models;

public sealed record ActiveExecutionEntry(
    ExecutionLeaseMetadata Lease,
    TimeSpan RunningDuration);
