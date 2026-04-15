using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetAdapter
{
    DesktopTargetKind Kind { get; }

    Task AttachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken);

    Task DetachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        CancellationToken cancellationToken);

    Task ValidateAttachmentAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken);

    Task ExecuteWorkItemAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        SessionWorkItem workItem,
        CancellationToken cancellationToken);

    Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken);
}
