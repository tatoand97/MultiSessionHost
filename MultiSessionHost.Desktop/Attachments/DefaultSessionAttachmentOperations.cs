using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;
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
    private readonly ISessionRecoveryStateStore _recoveryStateStore;
    private readonly IObservabilityRecorder _observabilityRecorder;

    public DefaultSessionAttachmentOperations(
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        ISessionAttachmentResolver attachmentResolver,
        IAttachedSessionStore attachedSessionStore,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry adapterRegistry,
        ISessionRecoveryStateStore recoveryStateStore,
        IObservabilityRecorder observabilityRecorder)
    {
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _attachmentResolver = attachmentResolver;
        _attachedSessionStore = attachedSessionStore;
        _targetProfileResolver = targetProfileResolver;
        _adapterRegistry = adapterRegistry;
        _recoveryStateStore = recoveryStateStore;
        _observabilityRecorder = observabilityRecorder;
    }

    public async Task<DesktopSessionAttachment> EnsureAttachedAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        CancellationToken cancellationToken)
    {
        var decision = await _recoveryStateStore.TryBeginAttemptAsync(snapshot.SessionId, SessionRecoveryAttemptKind.AttachmentEnsure, cancellationToken).ConfigureAwait(false);

        if (!decision.CanAttempt)
        {
            await _observabilityRecorder.RecordActivityAsync(snapshot.SessionId, "recovery.backoff.skipped_attempt", SessionObservabilityOutcome.Blocked.ToString(), TimeSpan.Zero, decision.ReasonCode, decision.Reason, nameof(DefaultSessionAttachmentOperations), new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["attemptKind"] = SessionRecoveryAttemptKind.AttachmentEnsure.ToString()
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(decision.Reason ?? "Recovery attempt is blocked.");
        }

        var adapter = _adapterRegistry.Resolve(context.Profile.Kind);
        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        var metadataDriftDetected = current is not null && !AreEquivalent(current.Target, context.Target);

        if (current is not null && AreEquivalent(current.Target, context.Target))
        {
            try
            {
                await adapter.ValidateAttachmentAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await HandleEnsureFailureAsync(snapshot.SessionId, exception, metadataDriftDetected, cancellationToken).ConfigureAwait(false);
                throw;
            }

            await _recoveryStateStore.RegisterSuccessAsync(snapshot.SessionId, "attachment-refresh", "recovery.success_cleared_failures", "Existing attachment remains valid.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            RuntimeObservability.RecoveryReattachDuration.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
            await _observabilityRecorder.RecordAttachmentAsync(snapshot.SessionId, "refresh", adapter.GetType().Name, SessionObservabilityOutcome.Success.ToString(), DateTimeOffset.UtcNow - startedAt, context.Profile.Kind.ToString(), null, null, nameof(DefaultSessionAttachmentOperations), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            return current;
        }

        if (current is not null)
        {
            if (metadataDriftDetected)
            {
                await _recoveryStateStore.MarkMetadataDriftDetectedAsync(snapshot.SessionId, "recovery.metadata_drift.detected", "Target metadata drift was detected during reattach.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            }

            await adapter.DetachAsync(snapshot, context, current, cancellationToken).ConfigureAwait(false);
            await _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);
        }

        DesktopSessionAttachment attachment;

        try
        {
            attachment = await _attachmentResolver.ResolveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await adapter.ValidateAttachmentAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
            await adapter.AttachAsync(snapshot, context, attachment, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleEnsureFailureAsync(snapshot.SessionId, exception, metadataDriftDetected, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _attachedSessionStore.SetAsync(attachment, cancellationToken).ConfigureAwait(false);
        if (metadataDriftDetected)
        {
            await _recoveryStateStore.MarkMetadataDriftClearedAsync(snapshot.SessionId, "recovery.metadata_drift.cleared", "Target metadata drift was resolved by reattach.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        }

        await _recoveryStateStore.RegisterSuccessAsync(snapshot.SessionId, current is null ? "attachment.ensure" : "attachment.reattach", "recovery.success_cleared_failures", current is null ? "Attachment ensure succeeded." : "Attachment reattach succeeded.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        RuntimeObservability.RecoveryReattachDuration.Record((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
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
        await _recoveryStateStore.MarkAttachmentInvalidAsync(sessionId, "recovery.attachment.invalidated", "Attachment was invalidated explicitly.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        await _recoveryStateStore.MarkSnapshotInvalidatedAsync(sessionId, "recovery.snapshot.invalidated", "Snapshot was invalidated alongside attachment invalidation.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
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

    private async Task HandleEnsureFailureAsync(SessionId sessionId, Exception exception, bool metadataDriftDetected, CancellationToken cancellationToken)
    {
        var category = metadataDriftDetected
            ? SessionRecoveryFailureCategory.MetadataDrift
            : exception is InvalidOperationException or ArgumentException
                ? SessionRecoveryFailureCategory.TargetInvalid
                : SessionRecoveryFailureCategory.AttachmentEnsureFailed;

        await _recoveryStateStore.RegisterFailureAsync(
            sessionId,
            category,
            "attachment.ensure-failed",
            category == SessionRecoveryFailureCategory.MetadataDrift ? "recovery.metadata_drift.detected" : category == SessionRecoveryFailureCategory.TargetInvalid ? "recovery.target.invalid" : "recovery.attachment.ensure.failed",
            exception.Message,
            new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);

        if (category == SessionRecoveryFailureCategory.TargetInvalid)
        {
            await _recoveryStateStore.MarkTargetQuarantinedAsync(sessionId, "recovery.target.quarantined", exception.Message, new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        }

        await _observabilityRecorder.RecordAdapterErrorAsync(sessionId, "attachment", "attachment.ensure", exception, category == SessionRecoveryFailureCategory.MetadataDrift ? "recovery.metadata_drift.detected" : "recovery.attachment.ensure.failed", nameof(DefaultSessionAttachmentOperations), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
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
