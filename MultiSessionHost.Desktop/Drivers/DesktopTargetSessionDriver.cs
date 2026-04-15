using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Drivers;

public sealed class DesktopTargetSessionDriver : ISessionDriver
{
    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;
    private readonly ISessionAttachmentOperations _attachmentOperations;
    private readonly IExecutionCoordinator _executionCoordinator;
    private readonly IExecutionResourceResolver _executionResourceResolver;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
    private readonly ISessionUiRefreshService _uiRefreshService;
    private readonly ISessionUiStateStore _sessionUiStateStore;

    public DesktopTargetSessionDriver(
        ISessionAttachmentRuntime sessionAttachmentRuntime,
        ISessionAttachmentOperations attachmentOperations,
        IExecutionCoordinator executionCoordinator,
        IExecutionResourceResolver executionResourceResolver,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry,
        ISessionUiRefreshService uiRefreshService,
        ISessionUiStateStore sessionUiStateStore)
    {
        _sessionAttachmentRuntime = sessionAttachmentRuntime;
        _attachmentOperations = attachmentOperations;
        _executionCoordinator = executionCoordinator;
        _executionResourceResolver = executionResourceResolver;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
        _uiRefreshService = uiRefreshService;
        _sessionUiStateStore = sessionUiStateStore;
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

        switch (workItem.Kind)
        {
            case SessionWorkItemKind.FetchUiSnapshot:
                await ExecuteWithLeaseAsync(
                    snapshot,
                    context,
                    workItem,
                    async (attachment, ct) => await _uiRefreshService.CaptureAsync(snapshot, context, attachment, ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.ProjectUiState:
                await ProjectUiStateAsync(snapshot, context, workItem, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await ExecuteWithLeaseAsync(
                    snapshot,
                    context,
                    workItem,
                    async (attachment, ct) =>
                    {
                        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
                        await adapter.ExecuteWorkItemAsync(snapshot, context, attachment, workItem, ct).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task ExecuteWithLeaseAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionWorkItem workItem,
        Func<DesktopSessionAttachment, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        var request = _executionResourceResolver.CreateForWorkItem(snapshot, context, workItem);

        await using var lease = await _executionCoordinator.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        var attachment = await _attachmentOperations.EnsureAttachedAsync(snapshot, context, cancellationToken).ConfigureAwait(false);
        await callback(attachment, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProjectUiStateAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        var uiState = await _sessionUiStateStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"UI state for session '{snapshot.SessionId}' was not initialized.");

        if (!string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
        {
            await _uiRefreshService.ProjectAsync(snapshot, context, attachment: null, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteWithLeaseAsync(
            snapshot,
            context,
            workItem,
            async (attachment, ct) => await _uiRefreshService.ProjectAsync(snapshot, context, attachment, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}
