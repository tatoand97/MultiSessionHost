using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentOperations : ISessionAttachmentOperations
{
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionAttachmentResolver _attachmentResolver;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _adapterRegistry;
    private readonly IObservabilityRecorder _observabilityRecorder;

    public DefaultSessionAttachmentOperations(
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        ISessionAttachmentResolver attachmentResolver,
        IAttachedSessionStore attachedSessionStore,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry,
        IObservabilityRecorder observabilityRecorder)
    {
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _attachmentResolver = attachmentResolver;
        _attachedSessionStore = attachedSessionStore;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
        _observabilityRecorder = observabilityRecorder;
    }

    public async Task<DesktopSessionAttachment> EnsureAttachedAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        CancellationToken cancellationToken)
    {
        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;

        if (current is not null && AreEquivalent(current.Target, context.Target))
        {
            await adapter.ValidateAttachmentAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAttachmentAsync(snapshot.SessionId, "refresh", adapter.GetType().Name, SessionObservabilityOutcome.Success.ToString(), DateTimeOffset.UtcNow - startedAt, context.Profile.Kind.ToString(), null, null, nameof(DefaultSessionAttachmentOperations), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
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
        await _observabilityRecorder.RecordAttachmentAsync(
            snapshot.SessionId,
            current is null ? "attach" : "reattach",
            adapter.GetType().Name,
            SessionObservabilityOutcome.Success.ToString(),
            DateTimeOffset.UtcNow - startedAt,
            context.Profile.Kind.ToString(),
            null,
            null,
            nameof(DefaultSessionAttachmentOperations),
            new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);

        return attachment;
    }

    public async Task<bool> InvalidateAsync(
        SessionId sessionId,
        DesktopSessionAttachment currentAttachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentAttachment);

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
                context = CreateFallbackContext(snapshot, currentAttachment);
            }

            var adapter = _adapterRegistry.Resolve(currentAttachment.Target.Kind);
            await adapter.DetachAsync(snapshot, context, currentAttachment, cancellationToken).ConfigureAwait(false);
        }

        await _attachedSessionStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await _observabilityRecorder.RecordAttachmentAsync(
            sessionId,
            "invalidate",
            currentAttachment.Target.GetType().Name,
            SessionObservabilityOutcome.Success.ToString(),
            TimeSpan.Zero,
            currentAttachment.Target.Kind.ToString(),
            null,
            null,
            nameof(DefaultSessionAttachmentOperations),
            new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
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
