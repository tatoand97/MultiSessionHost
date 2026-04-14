using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Scheduling;

public sealed class RoundRobinSessionScheduler : ISessionScheduler
{
    private readonly object _gate = new();
    private int _cursor;

    public IReadOnlyCollection<SchedulerDecision> GetDecisions(
        IReadOnlyCollection<SessionSnapshot> sessions,
        int maxGlobalParallelSessions,
        DateTimeOffset now)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var orderedSessions = sessions.ToArray();
        var decisions = new List<SchedulerDecision>(orderedSessions.Length * 2);
        var activeSessions = orderedSessions.Count(static snapshot => snapshot.CountsTowardsGlobalConcurrency);
        var startIndex = GetStartIndex(orderedSessions.Length);

        for (var offset = 0; offset < orderedSessions.Length; offset++)
        {
            var snapshot = orderedSessions[(startIndex + offset) % orderedSessions.Length];

            if (snapshot.CanStart(now) && activeSessions < maxGlobalParallelSessions)
            {
                decisions.Add(
                    new SchedulerDecision(
                        snapshot.SessionId,
                        SchedulerDecisionType.Start,
                        null,
                        "Session is eligible to start."));

                activeSessions++;
                continue;
            }

            var availableSlots = snapshot.Definition.MaxParallelWorkItems - snapshot.PendingWorkItems - snapshot.Runtime.InFlightWorkItems;

            if (availableSlots <= 0)
            {
                continue;
            }

            if (snapshot.ShouldEmitHeartbeat(now))
            {
                decisions.Add(
                    new SchedulerDecision(
                        snapshot.SessionId,
                        SchedulerDecisionType.EnqueueWork,
                        SessionWorkItem.Create(snapshot.SessionId, SessionWorkItemKind.Heartbeat, now, "Heartbeat interval elapsed."),
                        "Heartbeat due."));

                availableSlots--;
            }

            if (availableSlots > 0 && snapshot.ShouldEmitTick(now))
            {
                decisions.Add(
                    new SchedulerDecision(
                        snapshot.SessionId,
                        SchedulerDecisionType.EnqueueWork,
                        SessionWorkItem.Create(snapshot.SessionId, SessionWorkItemKind.Tick, now, "Tick interval elapsed."),
                        "Tick due."));
            }
        }

        return decisions;
    }

    private int GetStartIndex(int count)
    {
        lock (_gate)
        {
            if (_cursor >= count)
            {
                _cursor = 0;
            }

            var startIndex = _cursor;
            _cursor = (_cursor + 1) % count;
            return startIndex;
        }
    }
}
