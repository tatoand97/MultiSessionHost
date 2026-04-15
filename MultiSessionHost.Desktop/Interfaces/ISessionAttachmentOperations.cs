using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface ISessionAttachmentOperations
{
    Task<DesktopSessionAttachment> EnsureAttachedAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        CancellationToken cancellationToken);

    Task<bool> InvalidateAsync(
        SessionId sessionId,
        DesktopSessionAttachment currentAttachment,
        CancellationToken cancellationToken);
}
