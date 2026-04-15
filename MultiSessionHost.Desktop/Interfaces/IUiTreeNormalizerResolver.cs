using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Interfaces;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IUiTreeNormalizerResolver
{
    IUiTreeNormalizer Resolve(ResolvedDesktopTargetContext context);
}
