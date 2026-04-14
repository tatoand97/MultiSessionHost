namespace MultiSessionHost.Core.Models;

public sealed record SessionDefinition(
    SessionId Id,
    string DisplayName,
    bool Enabled,
    TimeSpan TickInterval,
    TimeSpan StartupDelay,
    int MaxParallelWorkItems,
    int MaxRetryCount,
    TimeSpan InitialBackoff,
    IReadOnlyCollection<string> Tags);
