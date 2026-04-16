using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Ocr;

public sealed record SessionOcrExtractionSummary(
    SessionId SessionId,
    DateTimeOffset ExtractedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    DateTimeOffset? SourcePreprocessingProcessedAtUtc,
    DesktopTargetKind TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string OcrProfileName,
    string OcrEngineName,
    string OcrEngineBackend,
    int TotalArtifactCount,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<OcrArtifactResultSummary> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
