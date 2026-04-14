using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionRuntimeState(
    SessionId SessionId,
    SessionStatus DesiredStatus,
    SessionStatus CurrentStatus,
    SessionStatus ObservedStatus,
    RetryPolicyState RetryPolicy,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    DateTimeOffset? LastTransitionAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastWorkItemScheduledAtUtc,
    DateTimeOffset? LastWorkItemCompletedAtUtc,
    DateTimeOffset? LastErrorAtUtc,
    string? LastError,
    int InFlightWorkItems)
{
    public static SessionRuntimeState Create(SessionDefinition definition, DateTimeOffset now) =>
        new(
            definition.Id,
            definition.Enabled ? SessionStatus.Running : SessionStatus.Stopped,
            SessionStatus.Created,
            SessionStatus.Created,
            RetryPolicyState.None,
            now,
            null,
            null,
            now,
            null,
            null,
            null,
            null,
            null,
            0);

    public bool IsActive =>
        CurrentStatus is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Paused or SessionStatus.Stopping;
}
