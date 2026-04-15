using System.Globalization;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

/// <summary>
/// Builds a summarized, policy-facing memory context from operational memory snapshots.
/// Centralizes translation from storage model to policy model.
/// </summary>
public interface IPolicyMemoryContextBuilder
{
    /// <summary>
    /// Builds a policy-facing memory context from an operational memory snapshot.
    /// </summary>
    PolicyMemoryContext Build(SessionOperationalMemorySnapshot? snapshot, SessionId sessionId, DateTimeOffset now);
}

/// <summary>
/// Default implementation that summarizes operational memory for policy consumption.
/// </summary>
public sealed class DefaultPolicyMemoryContextBuilder : IPolicyMemoryContextBuilder
{
    private readonly SessionHostOptions _options;

    public DefaultPolicyMemoryContextBuilder(SessionHostOptions options)
    {
        _options = options;
    }

    public PolicyMemoryContext Build(SessionOperationalMemorySnapshot? snapshot, SessionId sessionId, DateTimeOffset now)
    {
        if (snapshot is null || !_options.OperationalMemory.EnableOperationalMemory)
        {
            return PolicyMemoryContext.Empty(sessionId, now);
        }

        var warnings = new List<string>(snapshot.Warnings);

        // Build worksite summaries
        var worksiteSummaries = new List<WorksiteMemorySummary>();
        foreach (var worksite in snapshot.KnownWorksites)
        {
            var summary = BuildWorksiteSummary(worksite, snapshot, now);
            worksiteSummaries.Add(summary);
        }

        // Build risk summary
        var riskSummary = BuildRiskSummary(snapshot, now);

        // Build presence summary
        var presenceSummary = BuildPresenceSummary(snapshot, now);

        // Build timing summary
        var timingSummary = BuildTimingSummary(snapshot, now);

        // Build outcome summary
        var outcomeSummary = BuildOutcomeSummary(snapshot, now);

        var metadata = new Dictionary<string, string>(snapshot.Metadata, StringComparer.Ordinal)
        {
            ["capturedAtUtc"] = snapshot.CapturedAtUtc.UtcDateTime.ToString("O"),
            ["updatedAtUtc"] = snapshot.UpdatedAtUtc.UtcDateTime.ToString("O"),
            ["worksiteCount"] = worksiteSummaries.Count.ToString(CultureInfo.InvariantCulture),
            ["riskObservationCount"] = snapshot.RecentRiskObservations.Count.ToString(CultureInfo.InvariantCulture),
            ["presenceObservationCount"] = snapshot.RecentPresenceObservations.Count.ToString(CultureInfo.InvariantCulture),
            ["timingObservationCount"] = snapshot.RecentTimingObservations.Count.ToString(CultureInfo.InvariantCulture),
            ["outcomeObservationCount"] = snapshot.RecentOutcomeObservations.Count.ToString(CultureInfo.InvariantCulture)
        };

        return new PolicyMemoryContext(
            sessionId,
            snapshot.CapturedAtUtc,
            worksiteSummaries,
            riskSummary,
            presenceSummary,
            timingSummary,
            outcomeSummary,
            warnings,
            metadata);
    }

    private static WorksiteMemorySummary BuildWorksiteSummary(
        WorksiteObservation worksite,
        SessionOperationalMemorySnapshot snapshot,
        DateTimeOffset now)
    {
        var successRate = worksite.VisitCount > 0
            ? (double)worksite.SuccessCount / worksite.VisitCount
            : 0.0;

        // Determine if stale (older than 30 minutes)
        var isStaleTreshold = now.AddMinutes(-30);
        var isStale = worksite.IsStale || worksite.LastObservedAtUtc < isStaleTreshold;

        // Compute confidence from remembrance
        var confidence = worksite.LastKnownConfidence ?? 0.5;

        var metadata = new Dictionary<string, string>(worksite.Metadata, StringComparer.Ordinal)
        {
            ["visitCount"] = worksite.VisitCount.ToString(CultureInfo.InvariantCulture),
            ["successCount"] = worksite.SuccessCount.ToString(CultureInfo.InvariantCulture),
            ["failureCount"] = worksite.FailureCount.ToString(CultureInfo.InvariantCulture),
            ["occupancySignalCount"] = worksite.OccupancySignals.Count.ToString(CultureInfo.InvariantCulture)
        };

        return new WorksiteMemorySummary(
            worksite.WorksiteKey,
            worksite.WorksiteLabel,
            worksite.VisitCount,
            worksite.SuccessCount,
            worksite.FailureCount,
            successRate,
            worksite.LastOutcome,
            worksite.LastObservedRiskSeverity,
            worksite.LastSelectedAtUtc,
            worksite.LastArrivedAtUtc,
            worksite.OccupancySignals.Count,
            isStale,
            confidence,
            worksite.Tags,
            metadata);
    }

    private static RiskMemorySummary BuildRiskSummary(SessionOperationalMemorySnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.RecentRiskObservations.Count == 0)
        {
            return RiskMemorySummary.Empty();
        }

        // Find highest recent severity
        var highestSeverity = snapshot.RecentRiskObservations
            .Where(r => !r.IsStale)
            .Select(r => r.Severity)
            .DefaultIfEmpty(RiskSeverity.Unknown)
            .Aggregate((current, next) => CompareRiskSeverity(current, next) ? current : next);

        // Count repeated high-risk observations (count > 1 for Critical/High severity)
        var repeatedHighRiskCount = snapshot.RecentRiskObservations
            .Where(r => (r.Severity is RiskSeverity.Critical or RiskSeverity.High) && r.Count > 1)
            .Count();

        // Count repeated unknown observations
        var repeatedUnknownRiskCount = snapshot.RecentRiskObservations
            .Where(r => r.Severity is RiskSeverity.Unknown && r.Count > 1)
            .Count();

        // Top sources
        var topSources = snapshot.RecentRiskObservations
            .Where(r => r.SourceLabel is not null)
            .GroupBy(r => r.SourceLabel)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(3)
            .Select(g => g.Key ?? "unknown")
            .ToList();

        // Top suggested policies
        var topSuggestedPolicies = snapshot.RecentRiskObservations
            .Select(r => r.SuggestedPolicy.ToString())
            .GroupBy(p => p)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(3)
            .Select(g => g.Key)
            .ToArray();

        // Detect repeated withdraw-like pattern (multiple high-severity observations in recent period)
        var recentHighRisk = snapshot.RecentRiskObservations
            .Where(r => !r.IsStale && r.Severity is RiskSeverity.Critical or RiskSeverity.High)
            .Count();
        var hasWithdrawPattern = recentHighRisk >= 2;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["highestRecentSeverity"] = highestSeverity.ToString(),
            ["repeatedHighRiskCount"] = repeatedHighRiskCount.ToString(CultureInfo.InvariantCulture),
            ["totalRiskObservations"] = snapshot.RecentRiskObservations.Count.ToString(CultureInfo.InvariantCulture),
            ["recentHighRiskCount"] = recentHighRisk.ToString(CultureInfo.InvariantCulture)
        };

        return new RiskMemorySummary(
            highestSeverity,
            repeatedHighRiskCount,
            repeatedUnknownRiskCount,
            topSources.ToArray(),
            topSuggestedPolicies,
            hasWithdrawPattern,
            metadata);
    }

    private static PresenceMemorySummary BuildPresenceSummary(SessionOperationalMemorySnapshot snapshot, DateTimeOffset now)
    {
        var totalSignals = snapshot.RecentPresenceObservations.Count;
        
        // Recent = not stale
        var recentSignals = snapshot.RecentPresenceObservations
            .Where(p => !p.IsStale)
            .Count();

        var lastSignal = snapshot.RecentPresenceObservations
            .OrderByDescending(p => p.LastObservedAtUtc)
            .FirstOrDefault()
            ?.LastObservedAtUtc;

        // Recently occupied if there are recent presence signals
        var isRecentlyOccupied = recentSignals > 0;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["totalPresenceSignals"] = totalSignals.ToString(CultureInfo.InvariantCulture),
            ["recentPresenceSignals"] = recentSignals.ToString(CultureInfo.InvariantCulture),
            ["isRecentlyOccupied"] = isRecentlyOccupied.ToString()
        };

        return new PresenceMemorySummary(
            totalSignals,
            recentSignals,
            lastSignal,
            isRecentlyOccupied,
            metadata);
    }

    private static TimingMemorySummary BuildTimingSummary(SessionOperationalMemorySnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.RecentTimingObservations.Count == 0)
        {
            return TimingMemorySummary.Empty();
        }

        var timingKinds = snapshot.RecentTimingObservations
            .Select(t => t.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var avgTransitionDuration = snapshot.RecentTimingObservations
            .Where(t => t.Kind.Contains("transition", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.AverageDurationMs)
            .DefaultIfEmpty(0)
            .Average();

        var avgArrivalDelay = snapshot.RecentTimingObservations
            .Where(t => t.Kind.Contains("arrival", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.AverageDurationMs)
            .DefaultIfEmpty(0)
            .Average();

        var avgWaitWindow = snapshot.RecentTimingObservations
            .Where(t => t.Kind.Contains("wait", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.AverageDurationMs)
            .DefaultIfEmpty(0)
            .Average();

        // Repeated long wait pattern: has multiple wait observations with average > 5000ms
        var hasLongWaitPattern = snapshot.RecentTimingObservations
            .Where(t => t.Kind.Contains("wait", StringComparison.OrdinalIgnoreCase) && t.AverageDurationMs > 5000)
            .Count() >= 2;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["timingKindCount"] = timingKinds.Count.ToString(CultureInfo.InvariantCulture),
            ["avgTransitionDurationMs"] = avgTransitionDuration.ToString("0.##"),
            ["avgArrivalDelayMs"] = avgArrivalDelay.ToString("0.##"),
            ["avgWaitWindowMs"] = avgWaitWindow.ToString("0.##"),
            ["hasLongWaitPattern"] = hasLongWaitPattern.ToString()
        };

        return new TimingMemorySummary(
            timingKinds,
            avgTransitionDuration,
            avgArrivalDelay,
            avgWaitWindow,
            hasLongWaitPattern,
            metadata);
    }

    private static OutcomeMemorySummary BuildOutcomeSummary(SessionOperationalMemorySnapshot snapshot, DateTimeOffset now)
    {
        var outcomes = snapshot.RecentOutcomeObservations;
        var mostRecentKind = outcomes
            .OrderByDescending(o => o.ObservedAtUtc)
            .FirstOrDefault()
            ?.ResultKind;

        var successCount = outcomes.Count(o => o.ResultKind == "Success");
        var failureCount = outcomes.Count(o => o.ResultKind == "Failure");
        var deferredCount = outcomes.Count(o => o.ResultKind == "Deferred");
        var abortCount = outcomes.Count(o => o.ResultKind == "Abort");
        var noOpCount = outcomes.Count(o => o.ResultKind == "NoOp");

        // Detect recent failure pattern: has multiple failures in last N outcomes
        var recentOutcomes = outcomes
            .OrderByDescending(o => o.ObservedAtUtc)
            .Take(5)
            .ToList();
        var hasRecentFailurePattern = recentOutcomes.Count(o => o.ResultKind == "Failure") >= 2;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["successCount"] = successCount.ToString(CultureInfo.InvariantCulture),
            ["failureCount"] = failureCount.ToString(CultureInfo.InvariantCulture),
            ["deferredCount"] = deferredCount.ToString(CultureInfo.InvariantCulture),
            ["abortCount"] = abortCount.ToString(CultureInfo.InvariantCulture),
            ["noOpCount"] = noOpCount.ToString(CultureInfo.InvariantCulture),
            ["hasRecentFailurePattern"] = hasRecentFailurePattern.ToString()
        };

        return new OutcomeMemorySummary(
            mostRecentKind,
            successCount,
            failureCount,
            deferredCount,
            abortCount,
            noOpCount,
            hasRecentFailurePattern,
            metadata);
    }

    private static bool CompareRiskSeverity(RiskSeverity a, RiskSeverity b)
    {
        // Return true if a >= b in severity order
        var severityOrder = new[] { RiskSeverity.Critical, RiskSeverity.High, RiskSeverity.Moderate, RiskSeverity.Low, RiskSeverity.Unknown };
        var aIndex = Array.IndexOf(severityOrder, a);
        var bIndex = Array.IndexOf(severityOrder, b);
        return aIndex <= bIndex;
    }
}
