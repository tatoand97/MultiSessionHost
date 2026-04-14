using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionLifecycleManager
{
    Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task EnqueueAsync(SessionId sessionId, SessionWorkItem workItem, CancellationToken cancellationToken);

    Task StopAllAsync(CancellationToken cancellationToken);
}
