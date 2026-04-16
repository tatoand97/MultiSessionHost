using System.Globalization;
using System.Text.Json;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;

namespace MultiSessionHost.Desktop.Adapters;

public sealed class ScreenCaptureDesktopTargetAdapter : IDesktopTargetAdapter
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWindowFrameCapture _windowFrameCapture;
    private readonly IProcessLocator _processLocator;
    private readonly IWindowLocator _windowLocator;
    private readonly IObservabilityRecorder _observabilityRecorder;

    public ScreenCaptureDesktopTargetAdapter(
        IWindowFrameCapture windowFrameCapture,
        IProcessLocator processLocator,
        IWindowLocator windowLocator,
        IObservabilityRecorder observabilityRecorder)
    {
        _windowFrameCapture = windowFrameCapture;
        _processLocator = processLocator;
        _windowLocator = windowLocator;
        _observabilityRecorder = observabilityRecorder;
    }

    public DesktopTargetKind Kind => DesktopTargetKind.ScreenCaptureDesktop;

    public async Task AttachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await _observabilityRecorder.RecordActivityAsync(
            snapshot.SessionId,
            "screen.attach.started",
            SessionObservabilityOutcome.Success.ToString(),
            TimeSpan.Zero,
            null,
            null,
            nameof(ScreenCaptureDesktopTargetAdapter),
            Metadata(context, attachment),
            cancellationToken).ConfigureAwait(false);

        try
        {
            ValidateAttachmentCore(context, attachment);
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "screen.attach.succeeded",
                SessionObservabilityOutcome.Success.ToString(),
                DateTimeOffset.UtcNow - startedAt,
                null,
                null,
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "screen.attach.failed",
                SessionObservabilityOutcome.Failure.ToString(),
                DateTimeOffset.UtcNow - startedAt,
                "screen.attach.failed",
                exception.Message,
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(
                snapshot.SessionId,
                nameof(ScreenCaptureDesktopTargetAdapter),
                "screen.attach",
                exception,
                "screen.attach.failed",
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
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
                "screen.validate.failed",
                SessionObservabilityOutcome.Failure.ToString(),
                TimeSpan.Zero,
                "screen.validate.failed",
                exception.Message,
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(
                snapshot.SessionId,
                nameof(ScreenCaptureDesktopTargetAdapter),
                "screen.validate",
                exception,
                "screen.validate.failed",
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
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
        await _observabilityRecorder.RecordActivityAsync(
            snapshot.SessionId,
            "screen.capture.started",
            SessionObservabilityOutcome.Success.ToString(),
            TimeSpan.Zero,
            null,
            null,
            nameof(ScreenCaptureDesktopTargetAdapter),
            Metadata(context, attachment),
            cancellationToken).ConfigureAwait(false);

        try
        {
            ValidateAttachmentCore(context, attachment);
            var capturedFrame = await _windowFrameCapture.CaptureAsync(attachment, cancellationToken).ConfigureAwait(false);
            var duration = DateTimeOffset.UtcNow - startedAt;
            var metadata = BuildCaptureMetadata(context, attachment, capturedFrame, duration);
            var screenSnapshot = new ScreenSnapshot(
                snapshot.SessionId.Value,
                DateTimeOffset.UtcNow,
                attachment.Process.ProcessId,
                attachment.Process.ProcessName,
                attachment.Window.WindowHandle,
                attachment.Window.Title,
                capturedFrame.WindowBounds,
                capturedFrame.ImageWidth,
                capturedFrame.ImageHeight,
                capturedFrame.ImageFormat,
                capturedFrame.PixelFormat,
                capturedFrame.ImageBytes,
                metadata);
            var root = JsonSerializer.SerializeToElement(screenSnapshot, SnapshotJsonOptions);

            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "screen.capture.succeeded",
                SessionObservabilityOutcome.Success.ToString(),
                duration,
                null,
                null,
                nameof(ScreenCaptureDesktopTargetAdapter),
                metadata.Where(static pair => pair.Value is not null)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);

            return new UiSnapshotEnvelope(
                snapshot.SessionId.Value,
                screenSnapshot.CapturedAtUtc,
                attachment.Process,
                attachment.Window,
                root,
                metadata);
        }
        catch (Exception exception)
        {
            await _observabilityRecorder.RecordActivityAsync(
                snapshot.SessionId,
                "screen.capture.failed",
                SessionObservabilityOutcome.Failure.ToString(),
                DateTimeOffset.UtcNow - startedAt,
                "screen.capture.failed",
                exception.Message,
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordAdapterErrorAsync(
                snapshot.SessionId,
                nameof(ScreenCaptureDesktopTargetAdapter),
                "screen.capture",
                exception,
                "screen.capture.failed",
                nameof(ScreenCaptureDesktopTargetAdapter),
                Metadata(context, attachment),
                cancellationToken).ConfigureAwait(false);
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

    private static Dictionary<string, string?> BuildCaptureMetadata(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        WindowFrameCaptureResult capture,
        TimeSpan duration) =>
        new(StringComparer.Ordinal)
        {
            ["captureSource"] = "ScreenCapture",
            ["observabilityBackend"] = "ScreenCapture",
            ["targetKind"] = context.Profile.Kind.ToString(),
            ["adapter"] = nameof(ScreenCaptureDesktopTargetAdapter),
            ["captureBackend"] = capture.CaptureBackend,
            ["imageWidth"] = capture.ImageWidth.ToString(CultureInfo.InvariantCulture),
            ["imageHeight"] = capture.ImageHeight.ToString(CultureInfo.InvariantCulture),
            ["pixelFormat"] = capture.PixelFormat,
            ["imageFormat"] = capture.ImageFormat,
            ["captureSucceeded"] = true.ToString(),
            ["captureError"] = null,
            ["captureDurationMs"] = duration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
            ["targetWindowHandle"] = attachment.Window.WindowHandle.ToString(CultureInfo.InvariantCulture),
            ["targetProcessId"] = attachment.Process.ProcessId.ToString(CultureInfo.InvariantCulture),
            ["processName"] = attachment.Process.ProcessName,
            ["windowTitle"] = attachment.Window.Title,
            ["windowBoundsX"] = capture.WindowBounds.X.ToString(CultureInfo.InvariantCulture),
            ["windowBoundsY"] = capture.WindowBounds.Y.ToString(CultureInfo.InvariantCulture),
            ["windowBoundsWidth"] = capture.WindowBounds.Width.ToString(CultureInfo.InvariantCulture),
            ["windowBoundsHeight"] = capture.WindowBounds.Height.ToString(CultureInfo.InvariantCulture)
        };

    private static Dictionary<string, string> Metadata(ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment) =>
        new(StringComparer.Ordinal)
        {
            ["profileName"] = context.Profile.ProfileName,
            ["targetKind"] = context.Profile.Kind.ToString(),
            ["processId"] = attachment.Process.ProcessId.ToString(CultureInfo.InvariantCulture),
            ["processName"] = attachment.Process.ProcessName,
            ["windowHandle"] = attachment.Window.WindowHandle.ToString(CultureInfo.InvariantCulture),
            ["windowTitle"] = attachment.Window.Title
        };
}
