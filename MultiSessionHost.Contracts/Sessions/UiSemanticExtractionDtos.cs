namespace MultiSessionHost.Contracts.Sessions;

public sealed record UiSemanticExtractionResultDto(
    string SessionId,
    DateTimeOffset ExtractedAtUtc,
    IReadOnlyList<DetectedListDto> Lists,
    IReadOnlyList<DetectedTargetDto> Targets,
    IReadOnlyList<DetectedAlertDto> Alerts,
    IReadOnlyList<DetectedTransitStateDto> TransitStates,
    IReadOnlyList<DetectedResourceDto> Resources,
    IReadOnlyList<DetectedCapabilityDto> Capabilities,
    IReadOnlyList<DetectedPresenceEntityDto> PresenceEntities,
    IReadOnlyList<TargetSemanticPackageResultDto> Packages,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary);

public sealed record TargetSemanticPackageResultDto(
    string PackageName,
    string PackageVersion,
    bool Succeeded,
    string Confidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary,
    string? FailureReason,
    EveLikeSemanticPackageResultDto? EveLike);

public sealed record EveLikeSemanticPackageResultDto(
    string PackageName,
    string PackageVersion,
    LocalPresenceSnapshotDto Presence,
    TravelRouteSnapshotDto TravelRoute,
    IReadOnlyList<OverviewEntrySemanticDto> OverviewEntries,
    IReadOnlyList<ProbeScannerEntrySemanticDto> ProbeScannerEntries,
    TacticalSnapshotDto Tactical,
    SafetyLocationSemanticDto Safety,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary);

public sealed record LocalPresenceSnapshotDto(
    bool IsVisible,
    string? PanelLabel,
    int VisibleEntityCount,
    int? TotalEntityCount,
    IReadOnlyList<PresenceEntitySemanticDto> Entities,
    string Confidence,
    IReadOnlyList<string> Warnings);

public sealed record PresenceEntitySemanticDto(
    string? Label,
    string? Standing,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> SourceNodeIds,
    int? Count,
    string Confidence);

public sealed record TravelRouteSnapshotDto(
    bool RouteActive,
    string? DestinationLabel,
    string? CurrentLocationLabel,
    string? NextWaypointLabel,
    int WaypointCount,
    IReadOnlyList<string> VisibleWaypoints,
    double? ProgressPercent,
    string Confidence,
    IReadOnlyList<string> Reasons);

public sealed record OverviewEntrySemanticDto(
    string? Label,
    string? Category,
    string? DistanceText,
    double? DistanceValue,
    bool Selected,
    bool Targeted,
    string? Disposition,
    IReadOnlyList<string> SourceNodeIds,
    string Confidence,
    IReadOnlyList<string> Warnings);

public sealed record ProbeScannerEntrySemanticDto(
    string? Label,
    string? SignatureType,
    string? Status,
    string? DistanceText,
    double? DistanceValue,
    IReadOnlyList<string> SourceNodeIds,
    string Confidence,
    IReadOnlyList<string> Warnings);

public sealed record TacticalSnapshotDto(
    IReadOnlyList<OverviewEntrySemanticDto> PrimaryVisibleObjects,
    int HostileCandidateCount,
    IReadOnlyList<string> SelectedTargetLabels,
    IReadOnlyList<string> NearbyObjectHints,
    IReadOnlyList<string> EngagementAlerts,
    string Confidence,
    IReadOnlyList<string> Warnings);

public sealed record SafetyLocationSemanticDto(
    bool IsSafeLocation,
    string? SafeLocationLabel,
    bool HideAvailable,
    bool DockedHint,
    bool TetheredHint,
    string? EscapeRouteLabel,
    string Confidence,
    IReadOnlyList<string> Reasons);

public sealed record DetectedListDto(
    string NodeId,
    string? Label,
    int? ItemCount,
    int? SelectedItemCount,
    IReadOnlyList<string> VisibleItemLabels,
    bool IsScrollable,
    string Kind,
    string Confidence);

public sealed record DetectedTargetDto(
    string NodeId,
    string? Label,
    bool Selected,
    bool Active,
    bool Focused,
    int? Count,
    int? Index,
    string Kind,
    string Confidence);

public sealed record DetectedAlertDto(
    string NodeId,
    string Message,
    string Severity,
    bool Visible,
    string? SourceHint,
    string Confidence);

public sealed record DetectedTransitStateDto(
    string Status,
    IReadOnlyList<string> NodeIds,
    string? Label,
    double? ProgressPercent,
    IReadOnlyList<string> Reasons,
    string Confidence);

public sealed record DetectedResourceDto(
    string NodeId,
    string? Name,
    string Kind,
    double? Percent,
    double? Value,
    bool Degraded,
    bool Critical,
    string Confidence);

public sealed record DetectedCapabilityDto(
    string NodeId,
    string? Name,
    string Status,
    bool Enabled,
    bool Active,
    bool CoolingDown,
    string Confidence);

public sealed record DetectedPresenceEntityDto(
    string NodeId,
    string? Label,
    int? Count,
    IReadOnlyList<string> Membership,
    string Kind,
    string? Status,
    string Confidence);

public sealed record SemanticSummaryDto(
    string SessionId,
    DateTimeOffset ExtractedAtUtc,
    int ListCount,
    int TargetCount,
    int AlertCount,
    int TransitStateCount,
    int ResourceCount,
    int CapabilityCount,
    int PresenceEntityCount,
    int PackageCount,
    IReadOnlyList<string> PackageNames,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary);
