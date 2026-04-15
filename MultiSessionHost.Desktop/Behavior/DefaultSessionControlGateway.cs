using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class DefaultSessionControlGateway : ISessionControlGateway
{
    private readonly ISessionCoordinator _sessionCoordinator;

    public DefaultSessionControlGateway(ISessionCoordinator sessionCoordinator)
    {
        _sessionCoordinator = sessionCoordinator;
    }

    public async ValueTask PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Task.Run(() => _sessionCoordinator.PauseSessionAsync(sessionId, CancellationToken.None), CancellationToken.None);
        await ValueTask.CompletedTask;
    }

    public async ValueTask AbortSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Task.Run(() => _sessionCoordinator.StopSessionAsync(sessionId, CancellationToken.None), CancellationToken.None);
        await ValueTask.CompletedTask;
    }
}
