using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Ocr;

public sealed record OcrEngineResult(
    IReadOnlyList<OcrEngineTextFragment> Fragments,
    IReadOnlyList<OcrEngineTextLine> Lines,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record OcrEngineTextFragment(
    string Text,
    string? NormalizedText,
    double? Confidence,
    UiBounds? Bounds);

public sealed record OcrEngineTextLine(
    string Text,
    string? NormalizedText,
    double? Confidence,
    UiBounds? Bounds);
