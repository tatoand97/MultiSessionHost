using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IUiActionResolver
{
    ResolvedUiAction Resolve(UiTree tree, UiCommand command);
}
