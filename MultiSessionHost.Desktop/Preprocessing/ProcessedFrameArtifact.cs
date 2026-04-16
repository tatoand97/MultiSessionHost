namespace MultiSessionHost.Desktop.Preprocessing;

public sealed record ProcessedFrameArtifact(
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
    IReadOnlyDictionary<string, string?> Metadata,
    byte[] ImageBytes)
{
    public ProcessedFrameArtifactSummary ToSummary() =>
        new(
            ArtifactName,
            ArtifactKind,
            SourceSnapshotSequence,
            SourceRegionName,
            OutputWidth,
            OutputHeight,
            ImageFormat,
            PayloadByteLength,
            PreprocessingSteps,
            Warnings,
            Errors,
            Metadata);
}
