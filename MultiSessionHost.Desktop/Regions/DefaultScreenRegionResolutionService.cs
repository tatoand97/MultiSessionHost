using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Regions;

public sealed class DefaultScreenRegionResolutionService : IScreenRegionResolutionService
{
    private readonly ISessionScreenSnapshotStore _screenSnapshotStore;
    private readonly ISessionScreenRegionStore _screenRegionStore;
    private readonly IScreenRegionLocatorResolver _locatorResolver;
    private readonly ILogger<DefaultScreenRegionResolutionService> _logger;

    public DefaultScreenRegionResolutionService(
        ISessionScreenSnapshotStore screenSnapshotStore,
        ISessionScreenRegionStore screenRegionStore,
        IScreenRegionLocatorResolver locatorResolver,
        ILogger<DefaultScreenRegionResolutionService> logger)
    {
        _screenSnapshotStore = screenSnapshotStore;
        _screenRegionStore = screenRegionStore;
        _locatorResolver = locatorResolver;
        _logger = logger;
    }

    public async ValueTask<SessionScreenRegionResolution?> ResolveLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken)
    {
        if (context.Target.Kind != DesktopTargetKind.ScreenCaptureDesktop)
        {
            return null;
        }

        var regionLayoutProfile = DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.RegionLayoutProfile, "DefaultDesktopGrid");
        var locatorSetName = nameof(DefaultScreenRegionResolutionService);
        var resolvedAtUtc = DateTimeOffset.UtcNow;
        var snapshot = await _screenSnapshotStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            var failure = ScreenRegionResolutionModels.CreateFailure(null, context, regionLayoutProfile, locatorSetName, "screen-region-resolution-missing-snapshot", $"No screen snapshot is available for session '{sessionId}'.", resolvedAtUtc);
            await _screenRegionStore.UpsertLatestAsync(sessionId, failure, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"No screen snapshot is available for session '{sessionId}'.");
        }

        try
        {
            var locator = _locatorResolver.Resolve(context, snapshot, regionLayoutProfile);
            var locatorResult = await locator.ResolveAsync(snapshot, context, regionLayoutProfile, resolvedAtUtc, cancellationToken).ConfigureAwait(false);
            var resolution = ScreenRegionResolutionModels.Create(snapshot, context, regionLayoutProfile, locatorSetName, locatorResult);

            await _screenRegionStore.UpsertLatestAsync(sessionId, resolution, cancellationToken).ConfigureAwait(false);
            return resolution;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Region resolution failed for session '{SessionId}'.", sessionId);

            var failure = ScreenRegionResolutionModels.CreateFailure(snapshot, context, regionLayoutProfile, locatorSetName, nameof(DefaultScreenRegionResolutionService), exception.Message, resolvedAtUtc, [exception.Message]);
            await _screenRegionStore.UpsertLatestAsync(sessionId, failure, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}