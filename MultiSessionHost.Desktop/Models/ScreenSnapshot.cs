using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record ScreenSnapshot(
    string SessionId,
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
    IReadOnlyDictionary<string, string?> Metadata);
