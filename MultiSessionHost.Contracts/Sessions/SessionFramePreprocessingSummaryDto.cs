namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionFramePreprocessingSummaryDto(
    string SessionId,
    DateTimeOffset ProcessedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    string TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string PreprocessingProfileName,
    int TotalArtifactCount,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<ProcessedFrameArtifactSummaryDto> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
