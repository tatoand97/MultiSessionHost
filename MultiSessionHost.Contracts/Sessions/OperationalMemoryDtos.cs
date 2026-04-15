namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionOperationalMemorySnapshotDto(
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    SessionOperationalMemorySummaryDto Summary,
    IReadOnlyList<WorksiteObservationDto> KnownWorksites,
    IReadOnlyList<RiskObservationDto> RecentRiskObservations,
    IReadOnlyList<PresenceObservationDto> RecentPresenceObservations,
    IReadOnlyList<TimingObservationDto> RecentTimingObservations,
    IReadOnlyList<OutcomeObservationDto> RecentOutcomeObservations,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionOperationalMemorySummaryDto(
    int KnownWorksiteCount,
    int ActiveRiskMemoryCount,
    int ActivePresenceMemoryCount,
    int TimingObservationCount,
    int OutcomeObservationCount,
    DateTimeOffset LastUpdatedAtUtc,
    string TopRememberedRiskSeverity,
    string? MostRecentOutcomeKind);

public sealed record WorksiteObservationDto(
    string WorksiteKey,
    string? WorksiteLabel,
    IReadOnlyList<string> Tags,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    DateTimeOffset? LastSelectedAtUtc,
    DateTimeOffset? LastArrivedAtUtc,
    string? LastOutcome,
    string LastObservedRiskSeverity,
    IReadOnlyList<string> OccupancySignals,
    int VisitCount,
    int SuccessCount,
    int FailureCount,
    double? LastKnownConfidence,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RiskObservationDto(
    string ObservationId,
    string? EntityKey,
    string? EntityLabel,
    string? SourceKey,
    string? SourceLabel,
    string Severity,
    string SuggestedPolicy,
    string? RuleName,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int Count,
    double? LastKnownConfidence,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PresenceObservationDto(
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

public sealed record TimingObservationDto(
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

public sealed record OutcomeObservationDto(
    string OutcomeId,
    string? RelatedWorksiteKey,
    string? RelatedDirectiveKind,
    string? RelatedActivityState,
    string ResultKind,
    DateTimeOffset ObservedAtUtc,
    string? Message,
    bool IsStale,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MemoryObservationRecordDto(
    string ObservationId,
    string SessionId,
    string Category,
    string ObservationKey,
    DateTimeOffset ObservedAtUtc,
    string Source,
    string? Summary,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionOperationalMemoryHistoryDto(
    string SessionId,
    IReadOnlyList<MemoryObservationRecordDto> Entries);
