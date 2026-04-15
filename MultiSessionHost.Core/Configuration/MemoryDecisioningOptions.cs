using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Configuration;

/// <summary>
/// Configuration for memory-informed decisioning in the policy engine.
/// These options control how historical observations from operational memory
/// influence future policy decisions.
/// </summary>
public sealed class MemoryDecisioningOptions
{
    /// <summary>
    /// Enable/disable memory-informed decisioning globally.
    /// </summary>
    public bool EnableMemoryDecisioning { get; init; } = true;

    /// <summary>
    /// Site selection specific options.
    /// </summary>
    public SiteSelectionMemoryOptions SiteSelection { get; init; } = new();

    /// <summary>
    /// Threat response specific options.
    /// </summary>
    public ThreatMemoryOptions ThreatResponse { get; init; } = new();

    /// <summary>
    /// Transit/wait decision specific options.
    /// </summary>
    public TransitMemoryOptions Transit { get; init; } = new();

    /// <summary>
    /// Abort/recovery decision specific options.
    /// </summary>
    public AbortMemoryOptions Abort { get; init; } = new();
}

/// <summary>
/// Site selection memory options.
/// Controls how SelectNextSitePolicy uses worksite history and success rates.
/// </summary>
public sealed class SiteSelectionMemoryOptions
{
    /// <summary>
    /// Enable/disable site selection memory influence.
    /// </summary>
    public bool EnableMemoryInfluence { get; init; } = true;

    /// <summary>
    /// Boost ranking for worksites with successful outcomes.
    /// </summary>
    public bool PreferSuccessfulWorksites { get; init; } = true;

    /// <summary>
    /// Penalize/deprioritize worksites with repeated failures.
    /// </summary>
    public bool PenalizeFailedWorksites { get; init; } = true;

    /// <summary>
    /// Penalize/deprioritize worksites with repeated occupancy signals.
    /// </summary>
    public bool PenalizeOccupiedWorksites { get; init; } = true;

    /// <summary>
    /// Avoid worksites with high remembered risk severity.
    /// </summary>
    public bool AvoidHighRiskWorksites { get; init; } = true;

    /// <summary>
    /// Minimum number of successful visits to boost a worksite.
    /// </summary>
    public int MinimumSuccessfulVisits { get; init; } = 1;

    /// <summary>
    /// Weight for failure penalty (0-1 scale, higher = stronger penalty).
    /// </summary>
    public double FailurePenaltyWeight { get; init; } = 0.3;

    /// <summary>
    /// Weight for occupancy penalty (0-1 scale, higher = stronger penalty).
    /// </summary>
    public double OccupancyPenaltyWeight { get; init; } = 0.25;

    /// <summary>
    /// Weight for success boost (0-1 scale, higher = stronger boost).
    /// </summary>
    public double SuccessBoostWeight { get; init; } = 0.4;

    /// <summary>
    /// Time window in minutes for "recent" failure detection.
    /// </summary>
    public int RecentFailureWindowMinutes { get; init; } = 60;

    /// <summary>
    /// Time window in minutes for "recent" occupancy signal detection.
    /// </summary>
    public int RecentOccupancyWindowMinutes { get; init; } = 30;

    /// <summary>
    /// Stale memory handling: Ignore, SoftPenalty, or StrictPenalty.
    /// </summary>
    public string StaleMemoryPenaltyMode { get; init; } = "SoftPenalty";

    /// <summary>
    /// Risk severity threshold for avoidance (Critical, High, Moderate, Low, Unknown).
    /// </summary>
    public string AvoidWorksitesAboveRememberedRiskSeverity { get; init; } = "High";
}

/// <summary>
/// Threat response memory options.
/// Controls how ThreatResponsePolicy uses remembered risk patterns.
/// </summary>
public sealed class ThreatMemoryOptions
{
    /// <summary>
    /// Use remembered risk patterns to escalate threat response.
    /// </summary>
    public bool UseRepeatedRiskPattern { get; init; } = true;

    /// <summary>
    /// Withdraw if repeated high-risk pattern detected.
    /// </summary>
    public bool WithdrawOnRepeatedHighRisk { get; init; } = true;

    /// <summary>
    /// Minimum number of repeated high-risk observations to trigger pattern.
    /// </summary>
    public int RepeatedHighRiskThreshold { get; init; } = 2;

    /// <summary>
    /// Avoid current worksite if it has high remembered risk.
    /// </summary>
    public bool AvoidWorksiteWithRememberedRisk { get; init; } = true;

    /// <summary>
    /// Risk severity threshold for avoidance (Critical, High, Moderate, Low, Unknown).
    /// </summary>
    public string AvoidRiskSeverityThreshold { get; init; } = "High";
}

/// <summary>
/// Transit/wait decision memory options.
/// Controls how TransitPolicy uses timing and outcome history.
/// </summary>
public sealed class TransitMemoryOptions
{
    /// <summary>
    /// Use remembered timing patterns to decide wait vs. move-on.
    /// </summary>
    public bool UseTimingMemory { get; init; } = true;

    /// <summary>
    /// Threshold in milliseconds for "long wait" detection.
    /// </summary>
    public double LongWaitThresholdMs { get; init; } = 5000;

    /// <summary>
    /// Maximum remembered wait duration before deciding to move on.
    /// </summary>
    public double MaxRememberedWaitBeforeMoveOnMs { get; init; } = 10000;

    /// <summary>
    /// Allow behavior adjustment based on transition/arrival delays.
    /// </summary>
    public bool AdaptToRememberedDelays { get; init; } = true;
}

/// <summary>
/// Abort/recovery decision memory options.
/// Controls how policy escalates based on failure and recovery patterns.
/// </summary>
public sealed class AbortMemoryOptions
{
    /// <summary>
    /// Abort/pause after repeated failures if pattern detected.
    /// </summary>
    public bool AbortOnRepeatedFailures { get; init; } = true;

    /// <summary>
    /// Minimum number of failures in recent window to trigger abort.
    /// </summary>
    public int RepeatedFailureThreshold { get; init; } = 3;

    /// <summary>
    /// Time window in minutes for failure counting.
    /// </summary>
    public int FailureWindowMinutes { get; init; } = 60;

    /// <summary>
    /// Escalate abort priority when memory pattern reinforces decision.
    /// </summary>
    public int MemoryReinforceAbortPriorityBoost { get; init; } = 50;
}
