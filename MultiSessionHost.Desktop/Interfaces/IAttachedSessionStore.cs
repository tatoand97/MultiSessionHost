using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IAttachedSessionStore
{
    ValueTask<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    IReadOnlyCollection<DesktopSessionAttachment> GetAll();

    ValueTask SetAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
