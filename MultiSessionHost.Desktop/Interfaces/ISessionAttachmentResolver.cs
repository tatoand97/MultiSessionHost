using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface ISessionAttachmentResolver
{
    ValueTask<DesktopSessionAttachment> ResolveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken);
}
