using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IUiInteractionAdapter
{
    DesktopTargetKind Kind { get; }

    Task<UiInteractionResult> ClickAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);

    Task<UiInteractionResult> InvokeAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);

    Task<UiInteractionResult> SetTextAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);

    Task<UiInteractionResult> SelectItemAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);

    Task<UiInteractionResult> ToggleAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);
}
