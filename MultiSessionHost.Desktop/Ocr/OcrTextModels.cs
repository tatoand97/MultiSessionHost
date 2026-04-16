using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Ocr;

public sealed record OcrTextFragment(
    string Text,
    string NormalizedText,
    double? Confidence,
    UiBounds? Bounds,
    string SourceArtifactName,
    string? SourceRegionName);

public sealed record OcrTextLine(
    string Text,
    string NormalizedText,
    double? Confidence,
    UiBounds? Bounds,
    string SourceArtifactName,
    string? SourceRegionName);
