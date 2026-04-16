using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public enum DetectionConfidence
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum ListKind
{
    Unknown = 0,
    Items = 1,
    Options = 2,
    Results = 3,
    Navigation = 4,
    Presence = 5
}

public enum TargetKind
{
    Unknown = 0,
    SelectedItem = 1,
    ActiveItem = 2,
    FocusedElement = 3,
    ActionTarget = 4
}

public enum AlertSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public enum TransitStatus
{
    Unknown = 0,
    Idle = 1,
    InProgress = 2,
    Blocked = 3,
    Complete = 4
}

public enum ResourceKind
{
    Unknown = 0,
    Health = 1,
    Capacity = 2,
    Energy = 3,
    Charge = 4,
    Count = 5
}

public enum CapabilityStatus
{
    Unknown = 0,
    Enabled = 1,
    Disabled = 2,
    Active = 3,
    CoolingDown = 4
}

public enum PresenceEntityKind
{
    Unknown = 0,
    Item = 1,
    Person = 2,
    Device = 3,
    Service = 4,
    Group = 5
}

public sealed record UiSemanticClassification<TKind>(
    TKind Kind,
    DetectionConfidence Confidence,
    string? Rationale);

public sealed record DetectedList(
    string NodeId,
    string? Label,
    int? ItemCount,
    int? SelectedItemCount,
    IReadOnlyList<string> VisibleItemLabels,
    bool IsScrollable,
    ListKind Kind,
    DetectionConfidence Confidence);

public sealed record DetectedTarget(
    string NodeId,
    string? Label,
    bool Selected,
    bool Active,
    bool Focused,
    int? Count,
    int? Index,
    TargetKind Kind,
    DetectionConfidence Confidence);

public sealed record DetectedAlert(
    string NodeId,
    string Message,
    AlertSeverity Severity,
    bool Visible,
    string? SourceHint,
    DetectionConfidence Confidence);

public sealed record DetectedTransitState(
    TransitStatus Status,
    IReadOnlyList<string> NodeIds,
    string? Label,
    double? ProgressPercent,
    IReadOnlyList<string> Reasons,
    DetectionConfidence Confidence);

public sealed record DetectedResource(
    string NodeId,
    string? Name,
    ResourceKind Kind,
    double? Percent,
    double? Value,
    bool Degraded,
    bool Critical,
    DetectionConfidence Confidence);

public sealed record DetectedCapability(
    string NodeId,
    string? Name,
    CapabilityStatus Status,
    bool Enabled,
    bool Active,
    bool CoolingDown,
    DetectionConfidence Confidence);

public sealed record DetectedPresenceEntity(
    string NodeId,
    string? Label,
    int? Count,
    IReadOnlyList<string> Membership,
    PresenceEntityKind Kind,
    string? Status,
    DetectionConfidence Confidence);

public sealed record UiSemanticExtractionContribution(
    IReadOnlyList<DetectedList> Lists,
    IReadOnlyList<DetectedTarget> Targets,
    IReadOnlyList<DetectedAlert> Alerts,
    IReadOnlyList<DetectedTransitState> TransitStates,
    IReadOnlyList<DetectedResource> Resources,
    IReadOnlyList<DetectedCapability> Capabilities,
    IReadOnlyList<DetectedPresenceEntity> PresenceEntities,
    IReadOnlyList<string> Warnings)
{
    public static UiSemanticExtractionContribution Empty { get; } = new([], [], [], [], [], [], [], []);
}

public sealed record UiSemanticExtractionResult(
    SessionId SessionId,
    DateTimeOffset ExtractedAtUtc,
    IReadOnlyList<DetectedList> Lists,
    IReadOnlyList<DetectedTarget> Targets,
    IReadOnlyList<DetectedAlert> Alerts,
    IReadOnlyList<DetectedTransitState> TransitStates,
    IReadOnlyList<DetectedResource> Resources,
    IReadOnlyList<DetectedCapability> Capabilities,
    IReadOnlyList<DetectedPresenceEntity> PresenceEntities,
    IReadOnlyList<TargetSemanticPackageResult> Packages,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, DetectionConfidence> ConfidenceSummary)
{
    public static UiSemanticExtractionResult Empty(SessionId sessionId, DateTimeOffset now) =>
        new(sessionId, now, [], [], [], [], [], [], [], [], [], new Dictionary<string, DetectionConfidence>());
}

public sealed record UiSemanticExtractionContext(
    SessionId SessionId,
    SessionUiState SessionUiState,
    UiTree UiTree,
    SessionDomainState CurrentDomainState,
    SessionSnapshot SessionSnapshot,
    ResolvedDesktopTargetContext TargetContext,
    DesktopSessionAttachment? Attachment,
    DateTimeOffset Now);
