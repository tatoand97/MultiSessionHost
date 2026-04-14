using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionScheduler
{
    IReadOnlyCollection<SchedulerDecision> GetDecisions(
        IReadOnlyCollection<SessionSnapshot> sessions,
        int maxGlobalParallelSessions,
        DateTimeOffset now);
}
