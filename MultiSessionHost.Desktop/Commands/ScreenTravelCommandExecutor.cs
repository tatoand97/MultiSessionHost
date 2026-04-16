using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Commands;

public sealed class ScreenTravelCommandExecutor : IScreenTravelCommandExecutor
{
    private readonly ISessionScreenSnapshotStore _screenSnapshotStore;
    private readonly IScreenTravelInputDriver _inputDriver;
    private readonly ISessionRecoveryStateStore _recoveryStateStore;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly IClock _clock;
    private readonly ILogger<ScreenTravelCommandExecutor> _logger;

    public ScreenTravelCommandExecutor(
        ISessionScreenSnapshotStore screenSnapshotStore,
        IScreenTravelInputDriver inputDriver,
        ISessionRecoveryStateStore recoveryStateStore,
        IObservabilityRecorder observabilityRecorder,
        IClock clock,
        ILogger<ScreenTravelCommandExecutor> logger)
    {
        _screenSnapshotStore = screenSnapshotStore;
        _inputDriver = inputDriver;
        _recoveryStateStore = recoveryStateStore;
        _observabilityRecorder = observabilityRecorder;
        _clock = clock;
        _logger = logger;
    }

    public async Task<UiInteractionResult> ExecuteAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        UiCommand command,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profileName"] = context.Profile.ProfileName,
            ["targetKind"] = context.Profile.Kind.ToString(),
            ["commandKind"] = command.Kind.ToString()
        };

        if (!ScreenTravelCommandMetadata.TryParse(command.Metadata, out var travelMetadata, out var parseFailure))
        {
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelMetadataMissing, parseFailure ?? "Screen travel metadata could not be parsed.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }

        metadata["screenTravelIntent"] = travelMetadata!.TravelIntent.ToString();
        metadata["screenActionKind"] = travelMetadata.ActionKind;
        metadata["screenEvidenceSource"] = travelMetadata.EvidenceSource;
        metadata["screenSourceSnapshotSequence"] = travelMetadata.SourceSnapshotSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["screenSelectionConfidence"] = travelMetadata.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (context.Profile.Kind != DesktopTargetKind.ScreenCaptureDesktop)
        {
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelEvidenceInsufficient, "Screen travel commands can only execute for ScreenCaptureDesktop targets.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await _screenSnapshotStore.GetLatestAsync(attachment.SessionId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelSnapshotUnavailable, "No latest screen snapshot was available for screen travel execution.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }

        if (snapshot.Sequence != travelMetadata.SourceSnapshotSequence)
        {
            metadata["latestSnapshotSequence"] = snapshot.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelSnapshotMismatch, $"Screen travel metadata referenced snapshot sequence {travelMetadata.SourceSnapshotSequence} but the latest snapshot is {snapshot.Sequence}.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }

        var relativeBounds = travelMetadata.RelativeBounds;
        if (relativeBounds.Width <= 0 || relativeBounds.Height <= 0 || relativeBounds.X < 0 || relativeBounds.Y < 0 || relativeBounds.X + relativeBounds.Width > snapshot.ImageWidth || relativeBounds.Y + relativeBounds.Height > snapshot.ImageHeight)
        {
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelBoundsInvalid, "Screen travel bounds were outside the captured image.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }

        var absoluteX = snapshot.WindowBounds.X + relativeBounds.X + Math.Max(0, relativeBounds.Width / 2);
        var absoluteY = snapshot.WindowBounds.Y + relativeBounds.Y + Math.Max(0, relativeBounds.Height / 2);
        metadata["absoluteClickX"] = absoluteX.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["absoluteClickY"] = absoluteY.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await _observabilityRecorder.RecordActivityAsync(
            attachment.SessionId,
            "screen.travel.started",
            SessionObservabilityOutcome.Success.ToString(),
            TimeSpan.Zero,
            null,
            null,
            nameof(ScreenTravelCommandExecutor),
            metadata,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var clicked = await _inputDriver.ClickAsync(absoluteX, absoluteY, cancellationToken).ConfigureAwait(false);
            if (!clicked)
            {
                return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelInputFailed, "The screen travel input driver reported that the click was not delivered.", startedAt, metadata, cancellationToken).ConfigureAwait(false);
            }

            await _recoveryStateStore.RegisterSuccessAsync(
                attachment.SessionId,
                "screen.travel.click",
                "recovery.success_cleared_failures",
                $"Clicked screen travel target at ({absoluteX}, {absoluteY}).",
                metadata,
                cancellationToken).ConfigureAwait(false);

            var duration = _clock.UtcNow - startedAt;
            await _observabilityRecorder.RecordActivityAsync(
                attachment.SessionId,
                "screen.travel.succeeded",
                SessionObservabilityOutcome.Success.ToString(),
                duration,
                null,
                $"Clicked screen travel target at ({absoluteX}, {absoluteY}).",
                nameof(ScreenTravelCommandExecutor),
                metadata,
                cancellationToken).ConfigureAwait(false);

            return UiInteractionResult.Success($"Clicked screen travel target at ({absoluteX}, {absoluteY}).", _clock.UtcNow);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Screen travel click failed for session '{SessionId}'.", attachment.SessionId);
            return await FailAsync(command, attachment.SessionId, UiCommandFailureCodes.ScreenTravelInputFailed, exception.Message, startedAt, metadata, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UiInteractionResult> FailAsync(
        UiCommand command,
        SessionId sessionId,
        string failureCode,
        string message,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        await _recoveryStateStore.RegisterFailureAsync(
            sessionId,
            SessionRecoveryFailureCategory.CommandExecutionFailure,
            "screen.travel.click",
            failureCode,
            message,
            metadata,
            cancellationToken).ConfigureAwait(false);

        await _observabilityRecorder.RecordActivityAsync(
            sessionId,
            "screen.travel.failed",
            SessionObservabilityOutcome.Failure.ToString(),
            _clock.UtcNow - startedAt,
            failureCode,
            message,
            nameof(ScreenTravelCommandExecutor),
            metadata,
            cancellationToken).ConfigureAwait(false);

        return UiInteractionResult.Failure(message, failureCode, _clock.UtcNow);
    }
}
