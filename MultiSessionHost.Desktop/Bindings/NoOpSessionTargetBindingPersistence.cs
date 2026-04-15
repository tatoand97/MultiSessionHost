using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class NoOpSessionTargetBindingPersistence : ISessionTargetBindingPersistence
{
    public Task<IReadOnlyCollection<SessionTargetBinding>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<SessionTargetBinding>>([]);

    public Task SaveAsync(IReadOnlyCollection<SessionTargetBinding> bindings, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
