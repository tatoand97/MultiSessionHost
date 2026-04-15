using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Drivers;

public sealed class DesktopTargetSessionDriver : ISessionDriver
{
    private readonly SessionHostOptions _options;
    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
    private readonly IUiSnapshotSerializer _uiSnapshotSerializer;
    private readonly IUiTreeNormalizerResolver _uiTreeNormalizerResolver;
    private readonly IUiStateProjector _uiStateProjector;
    private readonly IWorkItemPlannerResolver _workItemPlannerResolver;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly IClock _clock;
    private readonly ILogger<DesktopTargetSessionDriver> _logger;

    public DesktopTargetSessionDriver(
        SessionHostOptions options,
        ISessionAttachmentRuntime sessionAttachmentRuntime,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry,
        IUiSnapshotSerializer uiSnapshotSerializer,
        IUiTreeNormalizerResolver uiTreeNormalizerResolver,
        IUiStateProjector uiStateProjector,
        IWorkItemPlannerResolver workItemPlannerResolver,
        ISessionUiStateStore sessionUiStateStore,
        IClock clock,
        ILogger<DesktopTargetSessionDriver> logger)
    {
        _options = options;
        _sessionAttachmentRuntime = sessionAttachmentRuntime;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
        _uiSnapshotSerializer = uiSnapshotSerializer;
        _uiTreeNormalizerResolver = uiTreeNormalizerResolver;
        _uiStateProjector = uiStateProjector;
        _workItemPlannerResolver = workItemPlannerResolver;
        _sessionUiStateStore = sessionUiStateStore;
        _clock = clock;
        _logger = logger;
    }

    public async Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _sessionAttachmentRuntime.EnsureAttachedAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _sessionAttachmentRuntime.InvalidateAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        var context = _targetProfileResolver.Resolve(snapshot);
        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
        var attachment = await _sessionAttachmentRuntime.EnsureAttachedAsync(snapshot, cancellationToken).ConfigureAwait(false);

        switch (workItem.Kind)
        {
            case SessionWorkItemKind.FetchUiSnapshot:
                EnsureUiSnapshotsEnabled();
                await FetchUiSnapshotAsync(snapshot.SessionId, snapshot, context, adapter, attachment, cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.ProjectUiState:
                EnsureUiSnapshotsEnabled();
                await ProjectUiStateAsync(snapshot.SessionId, snapshot, context, adapter, attachment, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await adapter.ExecuteWorkItemAsync(snapshot, context, attachment, workItem, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task FetchUiSnapshotAsync(
        SessionId sessionId,
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        IDesktopTargetAdapter adapter,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            var uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
            var rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);

            await _sessionUiStateStore.UpdateAsync(
                sessionId,
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
            await RecordUiRefreshErrorAsync(sessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ProjectUiStateAsync(
        SessionId sessionId,
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        IDesktopTargetAdapter adapter,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            var uiState = await _sessionUiStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"UI state for session '{sessionId}' was not initialized.");

            UiSnapshotEnvelope uiSnapshot;
            string rawJson;

            if (string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
            {
                uiSnapshot = await adapter.CaptureUiSnapshotAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
                rawJson = _uiSnapshotSerializer.Serialize(uiSnapshot);

                uiState = await _sessionUiStateStore.UpdateAsync(
                    sessionId,
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
                sessionId.Value,
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

            await _sessionUiStateStore.UpdateAsync(
                sessionId,
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
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(sessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
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

    private void EnsureUiSnapshotsEnabled()
    {
        if (!_options.EnableUiSnapshots)
        {
            throw new InvalidOperationException("UI snapshots are disabled. Set EnableUiSnapshots=true to request raw or projected UI state.");
        }
    }

}
