namespace MultiSessionHost.Contracts.Sessions;

public sealed record PolicyMemoryContextDto(
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<WorksiteMemorySummaryDto> KnownWorksites,
    RiskMemorySummaryDto RiskSummary,
    PresenceMemorySummaryDto PresenceSummary,
    TimingMemorySummaryDto TimingSummary,
    OutcomeMemorySummaryDto OutcomeSummary,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record WorksiteMemorySummaryDto(
    string WorksiteKey,
    string? WorksiteLabel,
    int VisitCount,
    int SuccessCount,
    int FailureCount,
    double SuccessRate,
    string? LastOutcome,
    string LastObservedRiskSeverity,
    DateTimeOffset? LastSelectedAtUtc,
    DateTimeOffset? LastArrivedAtUtc,
    int OccupancySignalCount,
    bool IsStale,
    double Confidence,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RiskMemorySummaryDto(
    string HighestRecentSeverity,
    int RepeatedHighRiskCount,
    int RepeatedUnknownRiskCount,
    IReadOnlyList<string> TopSources,
    IReadOnlyList<string> TopSuggestedPolicies,
    bool HasRepeatedWithdrawLikePattern,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PresenceMemorySummaryDto(
    int TotalPresenceSignals,
    int RecentPresenceSignals,
    DateTimeOffset? LastPresenceSignalAtUtc,
    bool IsRecentlyOccupied,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record TimingMemorySummaryDto(
    IReadOnlyList<string> KnownTimingKinds,
    double AverageTransitionDurationMs,
    double AverageArrivalDelayMs,
    double AverageWaitWindowMs,
    bool HasRepeatedLongWaitPattern,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OutcomeMemorySummaryDto(
    string? MostRecentOutcomeKind,
    int SuccessCount,
    int FailureCount,
    int DeferredCount,
    int AbortCount,
    int NoOpCount,
    bool HasRecentFailurePattern,
    IReadOnlyDictionary<string, string> Metadata);