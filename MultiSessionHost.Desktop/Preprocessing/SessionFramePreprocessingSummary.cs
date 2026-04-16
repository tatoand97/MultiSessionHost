using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public sealed record SessionFramePreprocessingSummary(
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
    IReadOnlyList<ProcessedFrameArtifactSummary> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
