using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Ocr;

public sealed record SessionOcrExtractionResult(
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
    IReadOnlyList<OcrArtifactResult> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public SessionOcrExtractionSummary ToSummary() =>
        new(
            SessionId,
            ExtractedAtUtc,
            SourceSnapshotSequence,
            SourceSnapshotCapturedAtUtc,
            SourceRegionResolutionSequence,
            SourceRegionResolutionResolvedAtUtc,
            SourcePreprocessingProcessedAtUtc,
            TargetKind,
            ObservabilityBackend,
            CaptureBackend,
            OcrProfileName,
            OcrEngineName,
            OcrEngineBackend,
            TotalArtifactCount,
            SuccessfulArtifactCount,
            FailedArtifactCount,
            Artifacts.Select(static artifact => artifact.ToSummary()).ToArray(),
            Warnings,
            Errors,
            Metadata);
}
