using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Behavior;

public enum TargetBehaviorPlanningStateKind
{
    Unknown = 0,
    NoRoute = 1,
    RouteReady = 2,
    AwaitingAutopilot = 3,
    AwaitingTravelTransition = 4,
    AwaitingRouteProgress = 5,
    Arrived = 6,
    BlockedByRisk = 7,
    BlockedByPolicy = 8,
    BlockedByRecovery = 9,
    StaleSemanticState = 10,
    RefreshRequired = 11,
    ObservabilityInsufficient = 12
}

public enum TravelAutopilotActionIntent
{
    None = 0,
    RefreshUi = 1,
    ToggleAutopilot = 2,
    SelectWaypoint = 3,
    InvokeTravelControl = 4
}

public sealed record TargetBehaviorPackSelection(
    string PackName,
    string MetadataKey);

public sealed record TargetBehaviorPlanningContext(
    SessionSnapshot SessionSnapshot,
    SessionUiState? SessionUiState,
    SessionDomainState SessionDomainState,
    DecisionPlan? CurrentDecisionPlan,
    UiSemanticExtractionResult? SemanticExtraction,
    RiskAssessmentResult? RiskAssessment,
    SessionRecoverySnapshot RecoverySnapshot,
    SessionActivitySnapshot? ActivitySnapshot,
    SessionOperationalMemorySnapshot? OperationalMemorySnapshot,
    SessionPolicyControlState PolicyControlState,
    ResolvedDesktopTargetContext TargetContext,
    DateTimeOffset Now);

public sealed record TravelAutopilotMemoryState(
    string? BehaviorPackName,
    string? RouteFingerprint,
    string? LastDestinationLabel,
    string? LastCurrentLocationLabel,
    string? LastNextWaypointLabel,
    string? LastActionCode,
    DateTimeOffset? LastActionAtUtc,
    double? LastObservedProgressPercent,
    int UnchangedRouteTickCount,
    DateTimeOffset? LastArrivalDetectedAtUtc,
    string? LastOutcomeCode)
{
    private const string BehaviorPackKey = "behavior.pack.selected";
    private const string RouteFingerprintKey = "behavior.travel.routeFingerprint";
    private const string DestinationKey = "behavior.travel.destinationLabel";
    private const string CurrentLocationKey = "behavior.travel.currentLocationLabel";
    private const string NextWaypointKey = "behavior.travel.nextWaypointLabel";
    private const string LastActionCodeKey = "behavior.travel.lastActionCode";
    private const string LastActionAtUtcKey = "behavior.travel.lastActionAtUtc";
    private const string LastObservedProgressKey = "behavior.travel.lastObservedProgressPercent";
    private const string UnchangedTickCountKey = "behavior.travel.unchangedRouteTickCount";
    private const string LastArrivalDetectedAtUtcKey = "behavior.travel.lastArrivalDetectedAtUtc";
    private const string LastOutcomeCodeKey = "behavior.travel.lastOutcomeCode";

    public static TravelAutopilotMemoryState Empty { get; } = new(null, null, null, null, null, null, null, null, 0, null, null);

    public static TravelAutopilotMemoryState FromMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new TravelAutopilotMemoryState(
            GetValue(metadata, BehaviorPackKey),
            GetValue(metadata, RouteFingerprintKey),
            GetValue(metadata, DestinationKey),
            GetValue(metadata, CurrentLocationKey),
            GetValue(metadata, NextWaypointKey),
            GetValue(metadata, LastActionCodeKey),
            GetDateTimeOffset(metadata, LastActionAtUtcKey),
            GetDouble(metadata, LastObservedProgressKey),
            GetInt(metadata, UnchangedTickCountKey) ?? 0,
            GetDateTimeOffset(metadata, LastArrivalDetectedAtUtcKey),
            GetValue(metadata, LastOutcomeCodeKey));
    }

    public Dictionary<string, string> ToMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(BehaviorPackName))
        {
            metadata[BehaviorPackKey] = BehaviorPackName!;
        }

        if (!string.IsNullOrWhiteSpace(RouteFingerprint))
        {
            metadata[RouteFingerprintKey] = RouteFingerprint!;
        }

        if (!string.IsNullOrWhiteSpace(LastDestinationLabel))
        {
            metadata[DestinationKey] = LastDestinationLabel!;
        }

        if (!string.IsNullOrWhiteSpace(LastCurrentLocationLabel))
        {
            metadata[CurrentLocationKey] = LastCurrentLocationLabel!;
        }

        if (!string.IsNullOrWhiteSpace(LastNextWaypointLabel))
        {
            metadata[NextWaypointKey] = LastNextWaypointLabel!;
        }

        if (!string.IsNullOrWhiteSpace(LastActionCode))
        {
            metadata[LastActionCodeKey] = LastActionCode!;
        }

        if (LastActionAtUtc is not null)
        {
            metadata[LastActionAtUtcKey] = LastActionAtUtc.Value.ToString("O");
        }

        if (LastObservedProgressPercent is not null)
        {
            metadata[LastObservedProgressKey] = LastObservedProgressPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        metadata[UnchangedTickCountKey] = UnchangedRouteTickCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (LastArrivalDetectedAtUtc is not null)
        {
            metadata[LastArrivalDetectedAtUtcKey] = LastArrivalDetectedAtUtc.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(LastOutcomeCode))
        {
            metadata[LastOutcomeCodeKey] = LastOutcomeCode!;
        }

        return metadata;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int? GetInt(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;

    private static double? GetDouble(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? GetDateTimeOffset(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}

public sealed record TravelAutopilotActionSelection(
    TravelAutopilotActionIntent Intent,
    UiCommand Command,
    string ActionCode,
    string ReasonCode,
    string Reason,
    IReadOnlyDictionary<string, string> Metadata,
    ScreenTravelActionTarget? ScreenTarget = null);

public sealed record ScreenActionAnchor(
    string? SourceRegionName,
    string? SourceArtifactName,
    string? CandidateLabel,
    UiBounds RelativeBounds,
    long SourceSnapshotSequence,
    double Confidence,
    string EvidenceSource,
    string Explanation,
    IReadOnlyDictionary<string, string> Diagnostics);

public sealed record ScreenTravelActionTarget(
    TravelAutopilotActionIntent Intent,
    string ActionKind,
    ScreenActionAnchor Anchor);

public sealed record TargetBehaviorPlanningState(
    string PackName,
    TargetBehaviorPlanningStateKind StateKind,
    string RouteFingerprint,
    bool RouteActive,
    bool RouteTransitioning,
    bool RouteArrived,
    bool PolicyPaused,
    bool RecoveryBlocked,
    bool RiskBlocked,
    string? DestinationLabel,
    string? CurrentLocationLabel,
    string? NextWaypointLabel,
    int WaypointCount,
    double? ProgressPercent,
    TravelAutopilotActionIntent ActionIntent,
    string? ActionCode,
    string ReasonCode,
    string Reason);

public sealed record TargetBehaviorPlanningResult(
    string? BehaviorPackName,
    string OutcomeCode,
    string ReasonCode,
    string Reason,
    DecisionPlan DecisionPlan,
    TargetBehaviorPlanningState State,
    TravelAutopilotMemoryState MemoryState,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool HasPlan => DecisionPlan.Directives.Count > 0 || DecisionPlan.Reasons.Count > 0 || DecisionPlan.Warnings.Count > 0;
}

public interface ITargetBehaviorPack
{
    string PackName { get; }

    string PackVersion { get; }

    ValueTask<TargetBehaviorPlanningResult> PlanAsync(TargetBehaviorPlanningContext context, CancellationToken cancellationToken);
}

public interface ITargetBehaviorPackResolver
{
    TargetBehaviorPackSelection? ResolveSelection(ResolvedDesktopTargetContext context);

    ITargetBehaviorPack? ResolvePack(string packName);
}

public interface ITravelAutopilotActionSelector
{
    ValueTask<TravelAutopilotActionSelection?> SelectActionAsync(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        CancellationToken cancellationToken);
}

public interface ITargetBehaviorPackPlanner
{
    ValueTask<TargetBehaviorPlanningResult?> TryPlanAsync(SessionId sessionId, CancellationToken cancellationToken);
}
