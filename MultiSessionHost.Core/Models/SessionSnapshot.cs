using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionSnapshot(
    SessionDefinition Definition,
    SessionRuntimeState Runtime,
    int PendingWorkItems)
{
    public SessionId SessionId => Definition.Id;

    public bool CountsTowardsGlobalConcurrency =>
        Runtime.CurrentStatus is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Paused or SessionStatus.Stopping;

    public bool CanStart(DateTimeOffset now) =>
        Runtime.DesiredStatus == SessionStatus.Running &&
        Runtime.CurrentStatus is SessionStatus.Created or SessionStatus.Stopped or SessionStatus.Faulted &&
        Runtime.RetryPolicy.IsRetryReady(now);

    public bool CanScheduleWork(DateTimeOffset now) =>
        Runtime.CurrentStatus == SessionStatus.Running &&
        Runtime.DesiredStatus == SessionStatus.Running &&
        Runtime.RetryPolicy.IsRetryReady(now) &&
        PendingWorkItems + Runtime.InFlightWorkItems < Definition.MaxParallelWorkItems;

    public bool ShouldEmitHeartbeat(DateTimeOffset now) =>
        CanScheduleWork(now) &&
        (Runtime.LastHeartbeatUtc is null || now - Runtime.LastHeartbeatUtc >= Definition.TickInterval);

    public bool ShouldEmitTick(DateTimeOffset now) =>
        CanScheduleWork(now) &&
        (Runtime.LastWorkItemScheduledAtUtc is null || now - Runtime.LastWorkItemScheduledAtUtc >= Definition.TickInterval);
}
