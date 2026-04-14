using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionDriver
{
    Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken);
}
