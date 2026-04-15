using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface IUiCommandExecutor
{
    Task<UiCommandResult> ExecuteAsync(UiCommand command, CancellationToken cancellationToken);
}
