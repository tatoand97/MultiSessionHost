using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Templates;

public sealed record VisualTemplateDefinition(
    string TemplateName,
    string TemplateKind,
    string TemplateSetName,
    IReadOnlyList<string> ExpectedSourceArtifactKinds,
    IReadOnlyList<string> PreferredRegions,
    double MatchingThreshold,
    string ImageFormat,
    byte[]? ImageBytes,
    string? ProviderReference,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record VisualTemplateSet(
    string TemplateSetName,
    string ProfileName,
    IReadOnlyList<VisualTemplateDefinition> Templates,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record TemplateMatch(
    string TemplateName,
    string TemplateKind,
    double Confidence,
    UiBounds Bounds,
    string SourceArtifactName,
    string? SourceRegionName,
    double? MatchScore,
    double Threshold,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record TemplateArtifactResult(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    int EvaluatedTemplateCount,
    int MatchedTemplateCount,
    IReadOnlyList<TemplateMatch> Matches,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public TemplateArtifactResultSummary ToSummary() =>
        new(
            ArtifactName,
            SourceRegionName,
            SourceArtifactKind,
            SelectionStrategy,
            UsedFullFrameFallback,
            EvaluatedTemplateCount,
            MatchedTemplateCount,
            Warnings,
            Errors,
            Metadata);
}

public sealed record TemplateArtifactResultSummary(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    int EvaluatedTemplateCount,
    int MatchedTemplateCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record SessionTemplateDetectionResult(
    SessionId SessionId,
    DateTimeOffset DetectedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    DateTimeOffset? SourcePreprocessingProcessedAtUtc,
    DateTimeOffset? SourceOcrExtractedAtUtc,
    DesktopTargetKind TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string DetectionProfileName,
    string TemplateSetName,
    string MatcherName,
    string MatcherBackend,
    int TotalArtifactCount,
    int TotalTemplatesEvaluated,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<TemplateArtifactResult> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public SessionTemplateDetectionSummary ToSummary() =>
        new(
            SessionId,
            DetectedAtUtc,
            SourceSnapshotSequence,
            SourceSnapshotCapturedAtUtc,
            SourceRegionResolutionSequence,
            SourceRegionResolutionResolvedAtUtc,
            SourcePreprocessingProcessedAtUtc,
            SourceOcrExtractedAtUtc,
            TargetKind,
            ObservabilityBackend,
            CaptureBackend,
            DetectionProfileName,
            TemplateSetName,
            MatcherName,
            MatcherBackend,
            TotalArtifactCount,
            TotalTemplatesEvaluated,
            SuccessfulArtifactCount,
            FailedArtifactCount,
            Artifacts.Select(static artifact => artifact.ToSummary()).ToArray(),
            Warnings,
            Errors,
            Metadata);
}

public sealed record SessionTemplateDetectionSummary(
    SessionId SessionId,
    DateTimeOffset DetectedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    long? SourceRegionResolutionSequence,
    DateTimeOffset? SourceRegionResolutionResolvedAtUtc,
    DateTimeOffset? SourcePreprocessingProcessedAtUtc,
    DateTimeOffset? SourceOcrExtractedAtUtc,
    DesktopTargetKind TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string DetectionProfileName,
    string TemplateSetName,
    string MatcherName,
    string MatcherBackend,
    int TotalArtifactCount,
    int TotalTemplatesEvaluated,
    int SuccessfulArtifactCount,
    int FailedArtifactCount,
    IReadOnlyList<TemplateArtifactResultSummary> Artifacts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
