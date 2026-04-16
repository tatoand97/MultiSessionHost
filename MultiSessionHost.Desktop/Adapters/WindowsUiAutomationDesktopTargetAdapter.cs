using System.Text.Json;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;

namespace MultiSessionHost.Desktop.Adapters;

public sealed class WindowsUiAutomationDesktopTargetAdapter : IDesktopTargetAdapter
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INativeUiAutomationReader _reader;
    private readonly IProcessLocator _processLocator;
    private readonly IWindowLocator _windowLocator;
    private readonly IObservabilityRecorder _observabilityRecorder;

    public WindowsUiAutomationDesktopTargetAdapter(
        INativeUiAutomationReader reader,
        IProcessLocator processLocator,
        IWindowLocator windowLocator,
        IObservabilityRecorder observabilityRecorder)
    {
        _reader = reader;
        _processLocator = processLocator;
        _windowLocator = windowLocator;
        _observabilityRecorder = observabilityRecorder;
    }

    public DesktopTargetKind Kind => DesktopTargetKind.WindowsUiAutomationDesktop;

    public async Task AttachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await _observabilityRecorder.RecordActivityAsync(
            snapshot.SessionId,
            "native.attach.started",
            SessionObservabilityOutcome.Success.ToString(),
            TimeSpan.Zero,
            null,
            null,
            nameof(WindowsUiAutomationDesktopTargetAdapter),
            Metadata(context, attachment),
            cancellationToken).ConfigureAwait(false);

        try
        {
            ValidateAttachmentCore(context, attachment);
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "native.attach.succeeded",
                SessionObservabilityOutcome.Success.ToString(),
                DateTimeOffset.UtcNow - startedAt,
                null,
                null,
                nameof(WindowsUiAutomationDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            RuntimeObservability.NativeAttachTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
        }
        catch (Exception exception)
        {
            RuntimeObservability.NativeAttachFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "native.attach.failed",
                SessionObservabilityOutcome.Failure.ToString(),
                DateTimeOffset.UtcNow - startedAt,
                "native.attach.failed",
                exception.Message,
                nameof(WindowsUiAutomationDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(snapshot.SessionId, nameof(WindowsUiAutomationDesktopTargetAdapter), "native.attach", exception, "native.attach.failed", nameof(WindowsUiAutomationDesktopTargetAdapter), Metadata(context, attachment), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public Task DetachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ValidateAttachmentAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateAttachmentCore(context, attachment);
        }
        catch (Exception exception)
        {
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "native.validate.failed",
                SessionObservabilityOutcome.Failure.ToString(),
                TimeSpan.Zero,
                "native.validate.failed",
                exception.Message,
                nameof(WindowsUiAutomationDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(snapshot.SessionId, nameof(WindowsUiAutomationDesktopTargetAdapter), "native.validate", exception, "native.validate.failed", nameof(WindowsUiAutomationDesktopTargetAdapter), Metadata(context, attachment), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public Task ExecuteWorkItemAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        SessionWorkItem workItem,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await _observabilityRecorder.RecordActivityAsync(snapshot.SessionId, "native.capture.started", SessionObservabilityOutcome.Success.ToString(), TimeSpan.Zero, null, null, nameof(WindowsUiAutomationDesktopTargetAdapter), Metadata(context, attachment), cancellationToken).ConfigureAwait(false);

        try
        {
            ValidateAttachmentCore(context, attachment);
            var options = NativeUiAutomationCaptureOptions.FromMetadata(context.Target.Metadata);
            var rawSnapshot = await _reader.CaptureAsync(attachment, options, cancellationToken).ConfigureAwait(false);
            var root = JsonSerializer.SerializeToElement(rawSnapshot.Root, SnapshotJsonOptions);
            var duration = DateTimeOffset.UtcNow - startedAt;
            var metadata = new Dictionary<string, string?>(rawSnapshot.Metadata, StringComparer.Ordinal)
            {
                ["targetKind"] = Kind.ToString(),
                ["adapter"] = nameof(WindowsUiAutomationDesktopTargetAdapter),
                ["nodeCount"] = rawSnapshot.NodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["actualMaxDepth"] = rawSnapshot.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["truncated"] = rawSnapshot.Truncated.ToString()
            };

            RuntimeObservability.NativeCaptureTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
            RuntimeObservability.NativeCaptureDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "native.capture.succeeded",
                SessionObservabilityOutcome.Success.ToString(),
                duration,
                null,
                null,
                nameof(WindowsUiAutomationDesktopTargetAdapter),
                metadata.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "native.identity.generated",
                SessionObservabilityOutcome.Success.ToString(),
                TimeSpan.Zero,
                null,
                null,
                nameof(WindowsUiAutomationDesktopTargetAdapter),
                metadata.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);

            if (HasFallbackIdentity(rawSnapshot.Root))
            {
                RuntimeObservability.NativeIdentityFallbackTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
                await _observabilityRecorder.RecordActivityAsync(snapshot.SessionId, "native.identity.fallback_used", SessionObservabilityOutcome.Success.ToString(), TimeSpan.Zero, null, null, nameof(WindowsUiAutomationDesktopTargetAdapter), metadata.Where(static pair => pair.Value is not null).ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            }

            return new UiSnapshotEnvelope(snapshot.SessionId.Value, DateTimeOffset.UtcNow, attachment.Process, attachment.Window, root, metadata);
        }
        catch (Exception exception)
        {
            RuntimeObservability.NativeCaptureFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
            await _observabilityRecorder.RecordActivityAsync(snapshot.SessionId, "native.capture.failed", SessionObservabilityOutcome.Failure.ToString(), DateTimeOffset.UtcNow - startedAt, "native.capture.failed", exception.Message, nameof(WindowsUiAutomationDesktopTargetAdapter), Metadata(context, attachment), cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(snapshot.SessionId, nameof(WindowsUiAutomationDesktopTargetAdapter), "native.capture", exception, "native.capture.failed", nameof(WindowsUiAutomationDesktopTargetAdapter), Metadata(context, attachment), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private void ValidateAttachmentCore(ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment)
    {
        if (attachment.Target.Kind != Kind || context.Target.Kind != Kind)
        {
            throw new InvalidOperationException($"Attachment target kind '{attachment.Target.Kind}' does not match adapter kind '{Kind}'.");
        }

        var process = _processLocator.GetProcessById(attachment.Process.ProcessId)
            ?? throw new InvalidOperationException($"The attached process '{attachment.Process.ProcessId}' is no longer running.");

        if (!string.Equals(process.ProcessName, attachment.Process.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The attached process '{attachment.Process.ProcessId}' changed from '{attachment.Process.ProcessName}' to '{process.ProcessName}'.");
        }

        var window = _windowLocator.GetWindowByHandle(attachment.Window.WindowHandle)
            ?? throw new InvalidOperationException($"The attached window '{attachment.Window.WindowHandle}' is no longer available.");

        if (window.ProcessId != attachment.Process.ProcessId)
        {
            throw new InvalidOperationException($"The attached window '{attachment.Window.WindowHandle}' now belongs to process '{window.ProcessId}' instead of '{attachment.Process.ProcessId}'.");
        }

        if (!window.IsVisible)
        {
            throw new InvalidOperationException($"The attached window '{attachment.Window.WindowHandle}' is not visible.");
        }
    }

    private static bool HasFallbackIdentity(NativeUiAutomationNode node) =>
        string.Equals(node.IdentityQuality, "Fallback", StringComparison.OrdinalIgnoreCase) ||
        node.Children.Any(HasFallbackIdentity);

    private static Dictionary<string, string> Metadata(ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment) =>
        new(StringComparer.Ordinal)
        {
            ["profileName"] = context.Profile.ProfileName,
            ["targetKind"] = context.Profile.Kind.ToString(),
            ["processId"] = attachment.Process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["processName"] = attachment.Process.ProcessName,
            ["windowHandle"] = attachment.Window.WindowHandle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["windowTitle"] = attachment.Window.Title
        };
}
