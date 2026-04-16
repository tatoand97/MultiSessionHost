using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public sealed record SessionFramePreprocessingResult(
    SessionId SessionId,
    DateTimeOffset ProcessedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    DesktopTargetKind TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string PreprocessingProfileName,
    int TotalArtifactCount,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<ProcessedFrameArtifact> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public SessionFramePreprocessingSummary ToSummary() =>
        new(
            SessionId,
            ProcessedAtUtc,
            SourceSnapshotSequence,
            SourceSnapshotCapturedAtUtc,
            SourceRegionResolutionSequence,
            SourceRegionResolutionResolvedAtUtc,
            TargetKind,
            ObservabilityBackend,
            CaptureBackend,
            PreprocessingProfileName,
            TotalArtifactCount,
            SuccessfulArtifactCount,
            FailedArtifactCount,
            Artifacts.Select(static artifact => artifact.ToSummary()).ToArray(),
            Warnings,
            Errors,
            Metadata);
}
