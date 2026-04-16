using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IScreenTravelCommandExecutor
{
    Task<UiInteractionResult> ExecuteAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        UiCommand command,
        CancellationToken cancellationToken);
}

public interface IScreenTravelInputDriver
{
    Task<bool> ClickAsync(int x, int y, CancellationToken cancellationToken);
}
