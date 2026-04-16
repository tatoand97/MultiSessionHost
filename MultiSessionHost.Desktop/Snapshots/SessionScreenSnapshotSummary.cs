using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed record SessionScreenSnapshotSummary(
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
    int PayloadByteLength,
    DesktopTargetKind TargetKind,
    string CaptureSource,
    string ObservabilityBackend,
    string? CaptureBackend,
    double? CaptureDurationMs,
    string CaptureOrigin,
    IReadOnlyDictionary<string, string?> Metadata);
