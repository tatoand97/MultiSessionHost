namespace MultiSessionHost.Desktop.Ocr;

public sealed record OcrArtifactResult(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    IReadOnlyList<string> PreprocessingSteps,
    string RecognizedText,
    string NormalizedText,
    double? Confidence,
    int FragmentCount,
    int LineCount,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    IReadOnlyList<OcrTextFragment> Fragments,
    IReadOnlyList<OcrTextLine> Lines,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public OcrArtifactResultSummary ToSummary() =>
        new(
            ArtifactName,
            SourceRegionName,
            SourceArtifactKind,
            PreprocessingSteps,
            RecognizedText,
            NormalizedText,
            Confidence,
            FragmentCount,
            LineCount,
            SelectionStrategy,
            UsedFullFrameFallback,
            Warnings,
            Errors,
            Metadata);
}

public sealed record OcrArtifactResultSummary(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    IReadOnlyList<string> PreprocessingSteps,
    string RecognizedText,
    string NormalizedText,
    double? Confidence,
    int FragmentCount,
    int LineCount,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
