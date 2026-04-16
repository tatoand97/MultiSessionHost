namespace MultiSessionHost.Desktop.Preprocessing;

public sealed record ProcessedFrameArtifactSummary(
    string ArtifactName,
    string ArtifactKind,
    long SourceSnapshotSequence,
    string? SourceRegionName,
    int OutputWidth,
    int OutputHeight,
    string ImageFormat,
    int PayloadByteLength,
    IReadOnlyList<string> PreprocessingSteps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
