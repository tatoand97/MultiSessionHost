using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingPersistence
{
    Task<IReadOnlyCollection<SessionTargetBinding>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyCollection<SessionTargetBinding> bindings, CancellationToken cancellationToken);
}
