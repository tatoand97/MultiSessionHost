using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingStore
{
    Task<IReadOnlyCollection<SessionTargetBinding>> GetAllAsync(CancellationToken cancellationToken);

    Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
