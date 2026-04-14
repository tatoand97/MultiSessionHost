using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task RunSchedulerCycleAsync(CancellationToken cancellationToken);

    Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    IReadOnlyCollection<SessionSnapshot> GetSessions();

    SessionSnapshot? GetSession(SessionId sessionId);

    ProcessHealthSnapshot GetProcessHealth();

    Task ShutdownAsync(CancellationToken cancellationToken);
}
