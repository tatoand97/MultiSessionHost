using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentRuntime : ISessionAttachmentRuntime
{
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionAttachmentResolver _attachmentResolver;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;

    public DefaultSessionAttachmentRuntime(
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        ISessionAttachmentResolver attachmentResolver,
        IAttachedSessionStore attachedSessionStore,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry)
    {
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _attachmentResolver = attachmentResolver;
        _attachedSessionStore = attachedSessionStore;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
    }

    public Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _attachedSessionStore.GetAsync(sessionId, cancellationToken).AsTask();

    public async Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var context = _targetProfileResolver.Resolve(snapshot);
        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

        if (current is not null && AreEquivalent(current.Target, context.Target))
        {
            await adapter.ValidateAttachmentAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            return current;
        }

        if (current is not null)
        {
            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            await _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
        }

        var attachment = await _attachmentResolver.ResolveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await adapter.ValidateAttachmentAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        await adapter.AttachAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        await _attachedSessionStore.SetAsync(attachment, cancellationToken).ConfigureAwait(false);

        return attachment;
    }

    public async Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var current = await _attachedSessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return false;
        }

        var definition = _sessionRegistry.GetById(sessionId);
        var state = await _sessionStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var snapshot = definition is not null && state is not null
            ? new SessionSnapshot(definition, state, PendingWorkItems: 0)
            : null;

        if (snapshot is not null)
        {
            ResolvedDesktopTargetContext context;

            try
            {
                context = _targetProfileResolver.Resolve(snapshot);
            }
            catch (InvalidOperationException)
            {
                context = CreateFallbackContext(snapshot, current);
            }

            var adapter = _adapterRegistry.Resolve(current.Target.Kind);
            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
        }

        await _attachedSessionStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static ResolvedDesktopTargetContext CreateFallbackContext(SessionSnapshot snapshot, DesktopSessionAttachment attachment)
    {
        var profile = new DesktopTargetProfile(
            attachment.Target.ProfileName,
            attachment.Target.Kind,
            attachment.Target.ProcessName,
            attachment.Target.WindowTitleFragment,
            attachment.Target.CommandLineFragment,
            attachment.Target.BaseAddress?.ToString(),
            attachment.Target.MatchingMode,
            attachment.Target.Metadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: attachment.BaseAddress is not null);
        var binding = new SessionTargetBinding(snapshot.SessionId, attachment.Target.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionId"] = snapshot.SessionId.Value
        };

        return new ResolvedDesktopTargetContext(snapshot.SessionId, profile, binding, attachment.Target, variables);
    }

    private static bool AreEquivalent(DesktopSessionTarget left, DesktopSessionTarget right) =>
        left.SessionId == right.SessionId &&
        left.ProfileName == right.ProfileName &&
        left.Kind == right.Kind &&
        left.MatchingMode == right.MatchingMode &&
        string.Equals(left.ProcessName, right.ProcessName, StringComparison.Ordinal) &&
        string.Equals(left.WindowTitleFragment, right.WindowTitleFragment, StringComparison.Ordinal) &&
        string.Equals(left.CommandLineFragment, right.CommandLineFragment, StringComparison.Ordinal) &&
        Equals(left.BaseAddress, right.BaseAddress) &&
        HaveSameMetadata(left.Metadata, right.Metadata);

    private static bool HaveSameMetadata(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
