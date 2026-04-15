using System.Globalization;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

/// <summary>
/// Helper utilities for memory-informed policy decisions.
/// Provides methods to compute memory influence factors and trace them.
/// </summary>
internal static class MemoryInfluenceHelpers
{
    /// <summary>
    /// Computes a ranking boost/penalty for a worksite based on memory.
    /// </summary>
    /// <remarks>
    /// Returns a value where:
    /// - Positive values boost the ranking (more likely to be selected)
    /// - Negative values penalize the ranking (less likely to be selected)
    /// - Zero means no memory-based adjustment
    /// </remarks>
    public static double ComputeWorksiteMemoryInfluence(
        WorksiteMemorySummary worksite,
        SiteSelectionMemoryOptions options,
        DateTimeOffset now)
    {
        if (!options.EnableMemoryInfluence)
        {
            return 0;
        }

        var influence = 0.0;

        // Boost for successful worksites
        if (options.PreferSuccessfulWorksites && worksite.SuccessCount >= options.MinimumSuccessfulVisits)
        {
            var successBoost = worksite.SuccessRate * options.SuccessBoostWeight;
            influence += successBoost;
        }

        // Penalize for repeated failures
        if (options.PenalizeFailedWorksites && worksite.FailureCount > 0)
        {
            var failureRatio = (double)worksite.FailureCount / (worksite.VisitCount + 1);
            var failurePenalty = -failureRatio * options.FailurePenaltyWeight;
            influence += failurePenalty;
        }

        // Penalize for occupancy signals
        if (options.PenalizeOccupiedWorksites && worksite.OccupancySignalCount > 0)
        {
            var occupancyPenalty = -options.OccupancyPenaltyWeight;
            influence += occupancyPenalty;
        }

        // Penalize for high remembered risk
        if (options.AvoidHighRiskWorksites && worksite.LastObservedRiskSeverity != RiskSeverity.Unknown)
        {
            var riskThreshold = ParseRiskSeverity(options.AvoidWorksitesAboveRememberedRiskSeverity);
            if (IsRiskSeverityAtLeast(worksite.LastObservedRiskSeverity, riskThreshold))
            {
                var riskPenalty = -0.5;
                influence += riskPenalty;
            }
        }

        // Penalize stale worksites
        if (worksite.IsStale)
        {
            var stalenessMode = options.StaleMemoryPenaltyMode;
            var stalePenalty = stalenessMode switch
            {
                "StrictPenalty" => -0.5,
                "SoftPenalty" => -0.1,
                _ => 0.0
            };
            influence += stalePenalty;
        }

        return influence;
    }

    /// <summary>
    /// Checks if memory indicates a repeated high-risk pattern.
    /// </summary>
    public static bool HasRepeatedHighRiskPattern(RiskMemorySummary risk, ThreatMemoryOptions options)
    {
        return options.UseRepeatedRiskPattern
            && options.WithdrawOnRepeatedHighRisk
            && risk.RepeatedHighRiskCount >= options.RepeatedHighRiskThreshold;
    }

    /// <summary>
    /// Checks if worksite has high remembered risk that should be avoided.
    /// </summary>
    public static bool ShouldAvoidWorksiteWithRememberedRisk(
        WorksiteMemorySummary worksite,
        ThreatMemoryOptions options)
    {
        if (!options.AvoidWorksiteWithRememberedRisk)
        {
            return false;
        }

        var threshold = ParseRiskSeverity(options.AvoidRiskSeverityThreshold);
        return IsRiskSeverityAtLeast(worksite.LastObservedRiskSeverity, threshold);
    }

    /// <summary>
    /// Checks if memory suggests a repeated failure pattern.
    /// </summary>
    public static bool HasRepeatedFailurePattern(OutcomeMemorySummary outcome, AbortMemoryOptions options)
    {
        return options.AbortOnRepeatedFailures
            && outcome.FailureCount >= options.RepeatedFailureThreshold;
    }

    /// <summary>
    /// Checks if timing memory indicates repeated long waits.
    /// </summary>
    public static bool HasRepeatedLongWaitPattern(TimingMemorySummary timing, TransitMemoryOptions options)
    {
        return options.UseTimingMemory
            && timing.HasRepeatedLongWaitPattern
            && timing.AverageWaitWindowMs >= options.LongWaitThresholdMs;
    }

    /// <summary>
    /// Creates a trace record for memory influence.
    /// </summary>
    public static MemoryInfluenceTrace CreateInfluenceTrace(
        string policyName,
        string influenceType,
        string memoryKey,
        string reasonCode,
        string reason,
        string value)
    {
        return new MemoryInfluenceTrace(
            policyName,
            influenceType,
            memoryKey,
            reasonCode,
            reason,
            value,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// Adds memory-influenced metadata to directive metadata.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AddMemoryInfluenceMetadata(
        IReadOnlyDictionary<string, string>? baseMetadata,
        WorksiteMemorySummary? worksite,
        bool memoryInfluenced = false)
    {
        var metadata = new Dictionary<string, string>(baseMetadata ?? new Dictionary<string, string>(), StringComparer.Ordinal);

        if (memoryInfluenced)
        {
            metadata["memoryInfluenced"] = "true";
        }

        if (worksite is not null)
        {
            metadata["memoryWorksiteKey"] = worksite.WorksiteKey;
            metadata["memorySuccessRate"] = worksite.SuccessRate.ToString("0.##");
            metadata["memoryVisitCount"] = worksite.VisitCount.ToString(CultureInfo.InvariantCulture);
            metadata["memorySuccessCount"] = worksite.SuccessCount.ToString(CultureInfo.InvariantCulture);
            metadata["memoryFailureCount"] = worksite.FailureCount.ToString(CultureInfo.InvariantCulture);
            metadata["memoryOccupancySignalCount"] = worksite.OccupancySignalCount.ToString(CultureInfo.InvariantCulture);
            metadata["memoryRiskSeverity"] = worksite.LastObservedRiskSeverity.ToString();
            metadata["memoryIsStale"] = worksite.IsStale.ToString();
        }

        return metadata;
    }

    private static RiskSeverity ParseRiskSeverity(string? value)
    {
        return value switch
        {
            "Critical" => RiskSeverity.Critical,
            "High" => RiskSeverity.High,
            "Moderate" => RiskSeverity.Moderate,
            "Low" => RiskSeverity.Low,
            "Unknown" => RiskSeverity.Unknown,
            _ => RiskSeverity.Unknown
        };
    }

    private static bool IsRiskSeverityAtLeast(RiskSeverity current, RiskSeverity threshold)
    {
        // Return true if current is >= threshold in severity
        var severityOrder = new[] { RiskSeverity.Critical, RiskSeverity.High, RiskSeverity.Moderate, RiskSeverity.Low, RiskSeverity.Unknown };
        var currentIndex = Array.IndexOf(severityOrder, current);
        var thresholdIndex = Array.IndexOf(severityOrder, threshold);
        return currentIndex <= thresholdIndex;
    }
}
