using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class DefaultSessionUiRefreshService : ISessionUiRefreshService
{
    private readonly SessionHostOptions _options;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
    private readonly IUiSnapshotSerializer _uiSnapshotSerializer;
    private readonly IUiTreeNormalizerResolver _uiTreeNormalizerResolver;
    private readonly IUiStateProjector _uiStateProjector;
    private readonly IWorkItemPlannerResolver _workItemPlannerResolver;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly ISessionDomainStateStore _sessionDomainStateStore;
    private readonly ISessionDomainStateProjectionService _domainStateProjectionService;
    private readonly IUiSemanticExtractionPipeline _semanticExtractionPipeline;
    private readonly ISessionSemanticExtractionStore _semanticExtractionStore;
    private readonly IClock _clock;
    private readonly ILogger<DefaultSessionUiRefreshService> _logger;

    public DefaultSessionUiRefreshService(
        SessionHostOptions options,
        IDesktopTargetAdapterRegistry adapterRegistry,
        IUiSnapshotSerializer uiSnapshotSerializer,
        IUiTreeNormalizerResolver uiTreeNormalizerResolver,
        IUiStateProjector uiStateProjector,
        IWorkItemPlannerResolver workItemPlannerResolver,
        ISessionUiStateStore sessionUiStateStore,
        ISessionDomainStateStore sessionDomainStateStore,
        ISessionDomainStateProjectionService domainStateProjectionService,
        IUiSemanticExtractionPipeline semanticExtractionPipeline,
        ISessionSemanticExtractionStore semanticExtractionStore,
        IClock clock,
        ILogger<DefaultSessionUiRefreshService> logger)
    {
        _options = options;
        _adapterRegistry = adapterRegistry;
        _uiSnapshotSerializer = uiSnapshotSerializer;
        _uiTreeNormalizerResolver = uiTreeNormalizerResolver;
        _uiStateProjector = uiStateProjector;
        _workItemPlannerResolver = workItemPlannerResolver;
        _sessionUiStateStore = sessionUiStateStore;
        _sessionDomainStateStore = sessionDomainStateStore;
        _domainStateProjectionService = domainStateProjectionService;
        _semanticExtractionPipeline = semanticExtractionPipeline;
        _semanticExtractionStore = semanticExtractionStore;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SessionUiState> CaptureAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        EnsureUiSnapshotsEnabled();

        try
        {
            var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
            var uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
            var rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);

            return await _sessionUiStateStore.UpdateAsync(
                snapshot.SessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SessionUiState> ProjectAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        CancellationToken cancellationToken)
    {
        EnsureUiSnapshotsEnabled();

        try
        {
            var uiState = await _sessionUiStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"UI state for session '{snapshot.SessionId}' was not initialized.");

            UiSnapshotEnvelope uiSnapshot;
            string rawJson;

            if (string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
            {
                if (attachment is null)
                {
                    throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a raw UI snapshot to project.");
                }

                var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
                uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
                rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);

                uiState = await _sessionUiStateStore.UpdateAsync(
                    snapshot.SessionId,
                    current => current with
                    {
                        RawSnapshotJson = rawJson,
                        LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                rawJson = uiState.RawSnapshotJson;
                uiSnapshot = _uiSnapshotSerializer.Deserialize(rawJson);
            }

            var source = DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.UiSource, context.Profile.ProfileName);
            var metadata = new UiSnapshotMetadata(
                snapshot.SessionId.Value,
                source,
                uiSnapshot.CapturedAtUtc,
                uiSnapshot.Process.ProcessId,
                uiSnapshot.Window.WindowHandle,
                uiSnapshot.Window.Title,
                uiSnapshot.Metadata);
            var treeNormalizer = _uiTreeNormalizerResolver.Resolve(context);
            var planner = _workItemPlannerResolver.Resolve(context);
            var tree = treeNormalizer.Normalize(metadata, uiSnapshot.Root);
            var diff = _uiStateProjector.Project(uiState.ProjectedTree, tree);
            var plannedWorkItems = planner.Plan(tree);

            var projectedUiState = await _sessionUiStateStore.UpdateAsync(
                snapshot.SessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = uiSnapshot.CapturedAtUtc,
                    ProjectedTree = tree,
                    LastDiff = diff,
                    PlannedWorkItems = plannedWorkItems,
                    LastRefreshCompletedAtUtc = _clock.UtcNow,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);

            var currentDomainState = await _sessionDomainStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Domain state for session '{snapshot.SessionId}' was not initialized.");
            var now = _clock.UtcNow;
            var semanticContext = new UiSemanticExtractionContext(
                snapshot.SessionId,
                projectedUiState,
                tree,
                currentDomainState,
                snapshot,
                context,
                attachment,
                now);
            var semanticExtraction = await _semanticExtractionPipeline.ExtractAsync(semanticContext, cancellationToken).ConfigureAwait(false);
            await _semanticExtractionStore.UpdateAsync(snapshot.SessionId, semanticExtraction, cancellationToken).ConfigureAwait(false);

            await _sessionDomainStateStore.UpdateAsync(
                snapshot.SessionId,
                current => _domainStateProjectionService.Project(
                    current,
                    snapshot,
                    context,
                    projectedUiState,
                    attachment,
                    semanticExtraction,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            return projectedUiState;
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            await RecordDomainRefreshErrorAsync(snapshot.SessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SessionUiState> RefreshAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        await CaptureAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        return await ProjectAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordUiRefreshErrorAsync(SessionId sessionId, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "UI refresh failed for session '{SessionId}'.", sessionId);

        await _sessionUiStateStore.UpdateAsync(
            sessionId,
            current => current with
            {
                LastRefreshError = exception.Message,
                LastRefreshErrorAtUtc = _clock.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordDomainRefreshErrorAsync(SessionId sessionId, Exception exception, CancellationToken cancellationToken)
    {
        await _sessionDomainStateStore.UpdateAsync(
            sessionId,
            current => _domainStateProjectionService.ProjectRefreshFailure(current, exception, _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    private void EnsureUiSnapshotsEnabled()
    {
        if (!_options.EnableUiSnapshots)
        {
            throw new InvalidOperationException("UI snapshots are disabled. Set EnableUiSnapshots=true to request raw or projected UI state.");
        }
    }
}
