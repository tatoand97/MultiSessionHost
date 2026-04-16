using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;

namespace MultiSessionHost.Desktop.Regions;

public interface IScreenRegionLocator
{
    string LocatorName { get; }

    string RegionLayoutProfile { get; }

    bool Supports(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile);

    ValueTask<ScreenRegionLocatorResult> ResolveAsync(
        SessionScreenSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        string regionLayoutProfile,
        DateTimeOffset resolvedAtUtc,
        CancellationToken cancellationToken);
}