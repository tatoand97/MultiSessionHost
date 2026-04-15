using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Persistence;

public interface IRuntimePersistenceBackend
{
    Task<RuntimePersistenceLoadResult> LoadAllAsync(CancellationToken cancellationToken);

    Task<SessionRuntimePersistenceEnvelope?> LoadSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task SaveSessionAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken);

    Task DeleteSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<string?> GetSessionPathAsync(SessionId sessionId, CancellationToken cancellationToken);
}

public interface IRuntimePersistenceCoordinator
{
    Task RehydrateAsync(CancellationToken cancellationToken);

    Task FlushSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task FlushAllAsync(CancellationToken cancellationToken);

    RuntimePersistenceStatusSnapshot GetStatus();
}
