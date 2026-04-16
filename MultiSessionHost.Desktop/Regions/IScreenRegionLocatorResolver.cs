using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;

namespace MultiSessionHost.Desktop.Regions;

public interface IScreenRegionLocatorResolver
{
    IScreenRegionLocator Resolve(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile);
}