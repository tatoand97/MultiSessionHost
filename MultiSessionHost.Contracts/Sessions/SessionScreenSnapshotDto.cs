using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionScreenSnapshotDto(
    string SessionId,
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
    string TargetKind,
    string CaptureSource,
    string ObservabilityBackend,
    string? CaptureBackend,
    double? CaptureDurationMs,
    string CaptureOrigin,
    byte[] ImageBytes,
    IReadOnlyDictionary<string, string?> Metadata);
