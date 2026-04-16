using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Automation;

public interface INativeUiAutomationReader
{
    Task<NativeUiAutomationRawSnapshot> CaptureAsync(
        DesktopSessionAttachment attachment,
        NativeUiAutomationCaptureOptions options,
        CancellationToken cancellationToken);
}
