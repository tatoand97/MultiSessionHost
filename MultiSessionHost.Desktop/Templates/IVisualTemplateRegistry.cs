using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Templates;

public interface IVisualTemplateRegistry
{
    VisualTemplateSet Resolve(ResolvedDesktopTargetContext context, TemplateDetectionProfile profile);
}
