using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Persistence;

public sealed class NoOpRuntimePersistenceBackend : IRuntimePersistenceBackend
{
    public Task<RuntimePersistenceLoadResult> LoadAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RuntimePersistenceLoadResult([], []));

    public Task<SessionRuntimePersistenceEnvelope?> LoadSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<SessionRuntimePersistenceEnvelope?>(null);

    public Task SaveSessionAsync(SessionRuntimePersistenceEnvelope envelope, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<string?> GetSessionPathAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
