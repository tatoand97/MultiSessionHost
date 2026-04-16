using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record WindowFrameCaptureResult(
    UiBounds WindowBounds,
    int ImageWidth,
    int ImageHeight,
    string ImageFormat,
    string? PixelFormat,
    byte[] ImageBytes,
    string CaptureBackend);
