namespace MultiSessionHost.Contracts.Sessions;

public sealed record OcrArtifactResultDto(
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
    IReadOnlyList<OcrTextFragmentDto> Fragments,
    IReadOnlyList<OcrTextLineDto> Lines,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
