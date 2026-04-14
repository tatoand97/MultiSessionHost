using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IUiSnapshotProvider
{
    Task<UiSnapshotEnvelope> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken);
}
