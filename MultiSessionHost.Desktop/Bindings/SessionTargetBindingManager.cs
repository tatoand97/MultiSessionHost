using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class SessionTargetBindingManager : ISessionTargetBindingManager
{
    private readonly ISessionTargetBindingStore _bindingStore;
    private readonly ISessionTargetBindingPersistence _persistence;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly IDesktopTargetProfileCatalog _profileCatalog;
    private readonly ISessionAttachmentRuntime _sessionAttachmentRuntime;

    public SessionTargetBindingManager(
        ISessionTargetBindingStore bindingStore,
        ISessionTargetBindingPersistence persistence,
        ISessionRegistry sessionRegistry,
        IDesktopTargetProfileCatalog profileCatalog,
        ISessionAttachmentRuntime sessionAttachmentRuntime)
    {
        _bindingStore = bindingStore;
        _persistence = persistence;
        _sessionRegistry = sessionRegistry;
        _profileCatalog = profileCatalog;
        _sessionAttachmentRuntime = sessionAttachmentRuntime;
    }

    public Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        _bindingStore.GetSnapshotAsync(cancellationToken);

    public Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _bindingStore.GetAsync(sessionId, cancellationToken);

    public async Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken)
    {
        var normalized = SessionTargetBindingModelMapper.NormalizeBinding(binding);
        ValidateBinding(normalized);

        var previous = await _bindingStore.GetAsync(normalized.SessionId, cancellationToken).ConfigureAwait(false);
        var upserted = await _bindingStore.UpsertAsync(normalized, cancellationToken).ConfigureAwait(false);

        try
        {
            await PersistSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RestorePreviousBindingAsync(previous, normalized.SessionId, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _sessionAttachmentRuntime.InvalidateAsync(normalized.SessionId, cancellationToken).ConfigureAwait(false);
        return upserted;
    }

    public async Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var previous = await _bindingStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (previous is null)
        {
            return false;
        }

        await _bindingStore.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);

        try
        {
            await PersistSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await _bindingStore.UpsertAsync(previous, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _sessionAttachmentRuntime.InvalidateAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ValidateBinding(SessionTargetBinding binding)
    {
        var configuredSessionIds = _sessionRegistry.GetAll()
            .Select(static definition => definition.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!SessionTargetBindingValidation.TryValidate(binding, configuredSessionIds, _profileCatalog, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _bindingStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        await _persistence.SaveAsync(snapshot.Bindings, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestorePreviousBindingAsync(
        SessionTargetBinding? previous,
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            await _bindingStore.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _bindingStore.UpsertAsync(previous, cancellationToken).ConfigureAwait(false);
    }
}
