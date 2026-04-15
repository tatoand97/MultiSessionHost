using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface ISessionAttachmentRuntime
{
    Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);

    Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken);
}
