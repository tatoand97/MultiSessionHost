using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface ISessionRegistry
{
    ValueTask RegisterAsync(SessionDefinition definition, CancellationToken cancellationToken);

    IReadOnlyCollection<SessionDefinition> GetAll();

    SessionDefinition? GetById(SessionId sessionId);
}
