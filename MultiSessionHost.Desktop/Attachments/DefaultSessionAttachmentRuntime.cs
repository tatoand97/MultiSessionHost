using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentRuntime : ISessionAttachmentRuntime
{
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IExecutionResourceResolver _executionResourceResolver;
    private readonly MultiSessionHost.Core.Interfaces.IExecutionCoordinator _executionCoordinator;
    private readonly ISessionAttachmentOperations _attachmentOperations;

    public DefaultSessionAttachmentRuntime(
        IAttachedSessionStore attachedSessionStore,
        IDesktopTargetProfileResolver targetProfileResolver,
        IExecutionResourceResolver executionResourceResolver,
        MultiSessionHost.Core.Interfaces.IExecutionCoordinator executionCoordinator,
        ISessionAttachmentOperations attachmentOperations)
    {
        _attachedSessionStore = attachedSessionStore;
        _targetProfileResolver = targetProfileResolver;
        _executionResourceResolver = executionResourceResolver;
        _executionCoordinator = executionCoordinator;
        _attachmentOperations = attachmentOperations;
    }

    public Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _attachedSessionStore.GetAsync(sessionId, cancellationToken).AsTask();

    public async Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var context = _targetProfileResolver.Resolve(snapshot);
        var request = _executionResourceResolver.CreateForAttachmentEnsure(snapshot, context);

        await using var lease = await _executionCoordinator.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        return await _attachmentOperations.EnsureAttachedAsync(snapshot, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var current = await _attachedSessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return false;
        }

        var request = _executionResourceResolver.CreateForAttachmentInvalidate(sessionId, current);

        await using var lease = await _executionCoordinator.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        return await _attachmentOperations.InvalidateAsync(sessionId, current, cancellationToken).ConfigureAwait(false);
    }
}
