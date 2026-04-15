using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Memory;

public enum MemoryObservationCategory
{
    Unknown = 0,
    Worksite = 1,
    Risk = 2,
    Presence = 3,
    Timing = 4,
    Outcome = 5
}

public sealed record SessionOperationalMemorySummary(
    int KnownWorksiteCount,
    int ActiveRiskMemoryCount,
    int ActivePresenceMemoryCount,
    int TimingObservationCount,
    int OutcomeObservationCount,
    DateTimeOffset LastUpdatedAtUtc,
    RiskSeverity TopRememberedRiskSeverity,
    string? MostRecentOutcomeKind);

public sealed record WorksiteObservation(
    string WorksiteKey,
    string? WorksiteLabel,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    DateTimeOffset? LastSelectedAtUtc,
    DateTimeOffset? LastArrivedAtUtc,
    string? LastOutcome,
    RiskSeverity LastObservedRiskSeverity,
    IReadOnlyList<string> OccupancySignals,
    int VisitCount,
    int SuccessCount,
    int FailureCount,
    double? LastKnownConfidence,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RiskObservation(
    string ObservationId,
    string? EntityKey,
    string? EntityLabel,
    string? SourceKey,
    string? SourceLabel,
    RiskSeverity Severity,
    RiskPolicySuggestion SuggestedPolicy,
    string? RuleName,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int Count,
    double? LastKnownConfidence,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PresenceObservation(
    string ObservationId,
    string EntityKey,
    string? EntityLabel,
    string? EntityType,
    string? Status,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int Count,
    double? LastKnownConfidence,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record TimingObservation(
    string TimingKey,
    string Kind,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    double LastDurationMs,
    int SampleCount,
    double MinDurationMs,
    double MaxDurationMs,
    double AverageDurationMs,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OutcomeObservation(
    string OutcomeId,
    string? RelatedWorksiteKey,
    string? RelatedDirectiveKind,
    string? RelatedActivityState,
    string ResultKind,
    DateTimeOffset ObservedAtUtc,
    string? Message,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryObservationRecord(
    string ObservationId,
    SessionId SessionId,
    MemoryObservationCategory Category,
    string ObservationKey,
    DateTimeOffset ObservedAtUtc,
    string Source,
    string? Summary,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionOperationalMemorySnapshot(
    SessionId SessionId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    SessionOperationalMemorySummary Summary,
    IReadOnlyList<WorksiteObservation> KnownWorksites,
    IReadOnlyList<RiskObservation> RecentRiskObservations,
    IReadOnlyList<PresenceObservation> RecentPresenceObservations,
    IReadOnlyList<TimingObservation> RecentTimingObservations,
    IReadOnlyList<OutcomeObservation> RecentOutcomeObservations,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static SessionOperationalMemorySnapshot Empty(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            now,
            now,
            new SessionOperationalMemorySummary(0, 0, 0, 0, 0, now, RiskSeverity.Unknown, null),
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record SessionOperationalMemoryUpdateContext(
    SessionId SessionId,
    SessionOperationalMemorySnapshot? PreviousSnapshot,
    SessionDomainState? DomainState,
    UiSemanticExtractionResult? SemanticExtraction,
    RiskAssessmentResult? RiskAssessment,
    DecisionPlan? DecisionPlan,
    DecisionPlanExecutionResult? ExecutionResult,
    SessionActivitySnapshot? ActivitySnapshot,
    DateTimeOffset Now);

public sealed record SessionOperationalMemoryUpdateResult(
    SessionOperationalMemorySnapshot? Snapshot,
    IReadOnlyList<MemoryObservationRecord> AddedObservationRecords,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata);
