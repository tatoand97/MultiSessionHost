using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed record TargetSemanticPackageSelection(
    string PackageName,
    string MetadataKey);

public sealed record TargetSemanticPackageContext(
    UiSemanticExtractionContext SemanticContext,
    UiSemanticExtractionResult GenericResult);

public interface ITargetSemanticPackage
{
    string PackageName { get; }

    string PackageVersion { get; }

    ValueTask<TargetSemanticPackageResult> ExtractAsync(TargetSemanticPackageContext context, CancellationToken cancellationToken);
}

public interface ITargetSemanticPackageResolver
{
    TargetSemanticPackageSelection? ResolveSelection(ResolvedDesktopTargetContext context);

    ITargetSemanticPackage? ResolvePackage(string packageName);
}

public sealed record TargetSemanticPackageResult(
    string PackageName,
    string PackageVersion,
    bool Succeeded,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, DetectionConfidence> ConfidenceSummary,
    string? FailureReason,
    EveLikeSemanticPackageResult? EveLike);

public sealed record PresenceEntitySemantic(
    string? Label,
    string? Standing,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> SourceNodeIds,
    int? Count,
    DetectionConfidence Confidence);

public sealed record LocalPresenceSnapshot(
    bool IsVisible,
    string? PanelLabel,
    int VisibleEntityCount,
    int? TotalEntityCount,
    IReadOnlyList<PresenceEntitySemantic> Entities,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Warnings);

public sealed record TravelRouteSnapshot(
    bool RouteActive,
    string? DestinationLabel,
    string? CurrentLocationLabel,
    string? NextWaypointLabel,
    int WaypointCount,
    IReadOnlyList<string> VisibleWaypoints,
    double? ProgressPercent,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Reasons);

public sealed record OverviewEntrySemantic(
    string? Label,
    string? Category,
    string? DistanceText,
    double? DistanceValue,
    bool Selected,
    bool Targeted,
    string? Disposition,
    IReadOnlyList<string> SourceNodeIds,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Warnings);

public sealed record ProbeScannerEntrySemantic(
    string? Label,
    string? SignatureType,
    string? Status,
    string? DistanceText,
    double? DistanceValue,
    IReadOnlyList<string> SourceNodeIds,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Warnings);

public sealed record TacticalSnapshot(
    IReadOnlyList<OverviewEntrySemantic> PrimaryVisibleObjects,
    int HostileCandidateCount,
    IReadOnlyList<string> SelectedTargetLabels,
    IReadOnlyList<string> NearbyObjectHints,
    IReadOnlyList<string> EngagementAlerts,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Warnings);

public sealed record SafetyLocationSemantic(
    bool IsSafeLocation,
    string? SafeLocationLabel,
    bool HideAvailable,
    bool DockedHint,
    bool TetheredHint,
    string? EscapeRouteLabel,
    DetectionConfidence Confidence,
    IReadOnlyList<string> Reasons);

public sealed record EveLikeSemanticPackageResult(
    string PackageName,
    string PackageVersion,
    LocalPresenceSnapshot Presence,
    TravelRouteSnapshot TravelRoute,
    IReadOnlyList<OverviewEntrySemantic> OverviewEntries,
    IReadOnlyList<ProbeScannerEntrySemantic> ProbeScannerEntries,
    TacticalSnapshot Tactical,
    SafetyLocationSemantic Safety,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, DetectionConfidence> ConfidenceSummary);