namespace MultiSessionHost.Core.Models;

public sealed record RetryPolicyState(
    int ConsecutiveFailures,
    int TotalRetriesScheduled,
    DateTimeOffset? NextRetryAtUtc,
    bool IsCircuitOpen,
    DateTimeOffset? OpenedAtUtc)
{
    public static RetryPolicyState None { get; } = new(0, 0, null, false, null);

    public bool IsRetryReady(DateTimeOffset now) =>
        !IsCircuitOpen || NextRetryAtUtc is null || now >= NextRetryAtUtc.Value;

    public bool HasExceeded(SessionDefinition definition) => ConsecutiveFailures > definition.MaxRetryCount;

    public TimeSpan GetNextBackoff(SessionDefinition definition)
    {
        var exponent = Math.Max(0, ConsecutiveFailures);
        var milliseconds = definition.InitialBackoff.TotalMilliseconds * Math.Pow(2d, exponent);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, int.MaxValue));
    }

    public RetryPolicyState RegisterFailure(SessionDefinition definition, DateTimeOffset now)
    {
        var nextFailureCount = ConsecutiveFailures + 1;

        if (nextFailureCount > definition.MaxRetryCount)
        {
            return this with
            {
                ConsecutiveFailures = nextFailureCount,
                IsCircuitOpen = true,
                OpenedAtUtc = now,
                NextRetryAtUtc = null
            };
        }

        var delay = TimeSpan.FromMilliseconds(
            Math.Min(
                definition.InitialBackoff.TotalMilliseconds * Math.Pow(2d, nextFailureCount - 1),
                int.MaxValue));

        return new RetryPolicyState(
            nextFailureCount,
            TotalRetriesScheduled + 1,
            now.Add(delay),
            true,
            now);
    }

    public RetryPolicyState Reset() =>
        new(
            0,
            TotalRetriesScheduled,
            null,
            false,
            null);
}
