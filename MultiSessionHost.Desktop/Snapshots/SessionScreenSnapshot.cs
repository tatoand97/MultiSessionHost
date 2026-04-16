using System.Globalization;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed record SessionScreenSnapshot(
    SessionId SessionId,
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    int ProcessId,
    string? ProcessName,
    long WindowHandle,
    string? WindowTitle,
    UiBounds WindowBounds,
    int ImageWidth,
    int ImageHeight,
    string ImageFormat,
    string? PixelFormat,
    byte[] ImageBytes,
    int PayloadByteLength,
    DesktopTargetKind TargetKind,
    string CaptureSource,
    string ObservabilityBackend,
    string? CaptureBackend,
    double? CaptureDurationMs,
    string CaptureOrigin,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public SessionScreenSnapshotSummary ToSummary() =>
        new(
            SessionId,
            Sequence,
            CapturedAtUtc,
            ProcessId,
            ProcessName,
            WindowHandle,
            WindowTitle,
            WindowBounds,
            ImageWidth,
            ImageHeight,
            ImageFormat,
            PixelFormat,
            PayloadByteLength,
            TargetKind,
            CaptureSource,
            ObservabilityBackend,
            CaptureBackend,
            CaptureDurationMs,
            CaptureOrigin,
            Metadata);

    public static SessionScreenSnapshot FromScreenSnapshot(
        ScreenSnapshot snapshot,
        DesktopTargetKind targetKind,
        long sequence,
        string captureOrigin = "LiveRefresh")
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var metadata = new Dictionary<string, string?>(snapshot.Metadata, StringComparer.Ordinal);

        return new SessionScreenSnapshot(
            SessionId.Parse(snapshot.SessionId),
            sequence,
            snapshot.CapturedAtUtc,
            snapshot.ProcessId,
            snapshot.ProcessName,
            snapshot.WindowHandle,
            snapshot.WindowTitle,
            snapshot.WindowBounds,
            snapshot.ImageWidth,
            snapshot.ImageHeight,
            snapshot.ImageFormat,
            snapshot.PixelFormat,
            snapshot.ImageBytes.ToArray(),
            snapshot.ImageBytes.Length,
            targetKind,
            GetMetadataValue(metadata, "captureSource", "ScreenCapture"),
            GetMetadataValue(metadata, "observabilityBackend", "ScreenCapture"),
            GetMetadataValue(metadata, "captureBackend"),
            TryParseDouble(metadata, "captureDurationMs"),
            captureOrigin,
            metadata);
    }

    private static string GetMetadataValue(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        string fallback = "") =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static double? TryParseDouble(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
