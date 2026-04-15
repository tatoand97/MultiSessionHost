using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingManager
{
    Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken);
}
