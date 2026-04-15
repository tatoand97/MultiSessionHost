using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetAdapterRegistry
{
    IDesktopTargetAdapter Resolve(DesktopTargetKind kind);
}
