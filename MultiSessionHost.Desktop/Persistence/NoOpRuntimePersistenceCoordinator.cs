using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Persistence;

public sealed class NoOpRuntimePersistenceCoordinator : IRuntimePersistenceCoordinator
{
    public Task RehydrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FlushSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FlushAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public RuntimePersistenceStatusSnapshot GetStatus() =>
        new(
            Enabled: false,
            Mode: "None",
            BasePath: null,
            SchemaVersion: 1,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Sessions: []);
}
