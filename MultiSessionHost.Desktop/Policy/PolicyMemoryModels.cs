using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

/// <summary>
/// Summarized, policy-facing memory context derived from operational memory.
/// This is a simplified view of persisted observations, designed for policy consumption.
/// </summary>
public sealed record PolicyMemoryContext(
    SessionId SessionId,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<WorksiteMemorySummary> KnownWorksites,
    RiskMemorySummary RiskSummary,
    PresenceMemorySummary PresenceSummary,
    TimingMemorySummary TimingSummary,
    OutcomeMemorySummary OutcomeSummary,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static PolicyMemoryContext Empty(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            now,
            [],
            RiskMemorySummary.Empty(),
            PresenceMemorySummary.Empty(),
            TimingMemorySummary.Empty(),
            OutcomeMemorySummary.Empty(),
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Summary of remembered observations about a specific worksite.
/// </summary>
public sealed record WorksiteMemorySummary(
    string WorksiteKey,
    string? WorksiteLabel,
    int VisitCount,
    int SuccessCount,
    int FailureCount,
    double SuccessRate,
    string? LastOutcome,
    RiskSeverity LastObservedRiskSeverity,
    DateTimeOffset? LastSelectedAtUtc,
    DateTimeOffset? LastArrivedAtUtc,
    int OccupancySignalCount,
    bool IsStale,
    double Confidence,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Summary of remembered risk observations across the session.
/// </summary>
public sealed record RiskMemorySummary(
    RiskSeverity HighestRecentSeverity,
    int RepeatedHighRiskCount,
    int RepeatedUnknownRiskCount,
    IReadOnlyList<string> TopSources,
    IReadOnlyList<string> TopSuggestedPolicies,
    bool HasRepeatedWithdrawLikePattern,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static RiskMemorySummary Empty() =>
        new(
            RiskSeverity.Unknown,
            0,
            0,
            [],
            [],
            false,
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Summary of remembered presence/occupancy observations.
/// </summary>
public sealed record PresenceMemorySummary(
    int TotalPresenceSignals,
    int RecentPresenceSignals,
    DateTimeOffset? LastPresenceSignalAtUtc,
    bool IsRecentlyOccupied,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static PresenceMemorySummary Empty() =>
        new(
            0,
            0,
            null,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Summary of remembered timing/transition observations.
/// </summary>
public sealed record TimingMemorySummary(
    IReadOnlyList<string> KnownTimingKinds,
    double AverageTransitionDurationMs,
    double AverageArrivalDelayMs,
    double AverageWaitWindowMs,
    bool HasRepeatedLongWaitPattern,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static TimingMemorySummary Empty() =>
        new(
            [],
            0,
            0,
            0,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Summary of remembered outcome observations.
/// </summary>
public sealed record OutcomeMemorySummary(
    string? MostRecentOutcomeKind,
    int SuccessCount,
    int FailureCount,
    int DeferredCount,
    int AbortCount,
    int NoOpCount,
    bool HasRecentFailurePattern,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static OutcomeMemorySummary Empty() =>
        new(
            null,
            0,
            0,
            0,
            0,
            0,
            false,
            new Dictionary<string, string>(StringComparer.Ordinal));
}

/// <summary>
/// Records how a specific memory factor influenced a policy decision.
/// Included in policy explanations when memory materially affects the outcome.
/// </summary>
public sealed record MemoryInfluenceTrace(
    string PolicyName,
    string InfluenceType,
    string MemoryKey,
    string ReasonCode,
    string Reason,
    string Value,
    IReadOnlyDictionary<string, string> Metadata);
