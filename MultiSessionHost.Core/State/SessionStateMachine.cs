using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.State;

public static class SessionStateMachine
{
    private static readonly IReadOnlyDictionary<SessionStatus, SessionStatus[]> AllowedTransitions =
        new Dictionary<SessionStatus, SessionStatus[]>
        {
            [SessionStatus.Created] = [SessionStatus.Starting, SessionStatus.Stopped, SessionStatus.Faulted],
            [SessionStatus.Starting] = [SessionStatus.Running, SessionStatus.Paused, SessionStatus.Stopping, SessionStatus.Faulted],
            [SessionStatus.Running] = [SessionStatus.Paused, SessionStatus.Stopping, SessionStatus.Faulted],
            [SessionStatus.Paused] = [SessionStatus.Running, SessionStatus.Stopping, SessionStatus.Faulted],
            [SessionStatus.Stopping] = [SessionStatus.Stopped, SessionStatus.Faulted],
            [SessionStatus.Stopped] = [SessionStatus.Starting],
            [SessionStatus.Faulted] = [SessionStatus.Starting, SessionStatus.Stopped]
        };

    public static bool CanTransition(SessionStatus fromStatus, SessionStatus toStatus) =>
        fromStatus == toStatus ||
        (AllowedTransitions.TryGetValue(fromStatus, out var allowedStatuses) && allowedStatuses.Contains(toStatus));

    public static SessionRuntimeState Transition(
        SessionRuntimeState current,
        SessionStatus nextStatus,
        DateTimeOffset now,
        string? lastError = null)
    {
        if (!CanTransition(current.CurrentStatus, nextStatus))
        {
            throw new InvalidOperationException(
                $"Invalid session state transition from '{current.CurrentStatus}' to '{nextStatus}' for '{current.SessionId}'.");
        }

        return current with
        {
            CurrentStatus = nextStatus,
            ObservedStatus = nextStatus,
            StartedAtUtc = nextStatus == SessionStatus.Running ? current.StartedAtUtc ?? now : current.StartedAtUtc,
            StoppedAtUtc = nextStatus is SessionStatus.Stopped or SessionStatus.Faulted ? now : current.StoppedAtUtc,
            LastTransitionAtUtc = now,
            LastError = lastError ?? current.LastError,
            LastErrorAtUtc = lastError is null ? current.LastErrorAtUtc : now
        };
    }
}
