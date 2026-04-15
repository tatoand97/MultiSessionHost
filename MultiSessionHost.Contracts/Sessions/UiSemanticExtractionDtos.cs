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
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary);

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
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> ConfidenceSummary);
