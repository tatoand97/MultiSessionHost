namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionOcrExtractionSummaryDto(
    string SessionId,
    DateTimeOffset ExtractedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    DateTimeOffset? SourcePreprocessingProcessedAtUtc,
    string TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string OcrProfileName,
    string OcrEngineName,
    string OcrEngineBackend,
    int TotalArtifactCount,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<OcrArtifactResultSummaryDto> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
