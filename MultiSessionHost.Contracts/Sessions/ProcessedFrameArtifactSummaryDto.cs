namespace MultiSessionHost.Contracts.Sessions;

public sealed record ProcessedFrameArtifactSummaryDto(
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
