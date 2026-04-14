namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionStateDto(
    string CurrentStatus,
    string DesiredStatus,
    string ObservedStatus,
    int PendingWorkItems,
    int InFlightWorkItems,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastWorkItemCompletedAtUtc,
    DateTimeOffset? LastErrorAtUtc,
    string? LastError);
