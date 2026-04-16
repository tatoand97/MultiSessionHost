using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Regions;

public interface IScreenRegionResolutionService
{
    ValueTask<SessionScreenRegionResolution?> ResolveLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken);
}