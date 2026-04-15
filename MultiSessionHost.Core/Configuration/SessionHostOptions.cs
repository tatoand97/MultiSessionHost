using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Configuration;

public sealed class SessionHostOptions
{
    public const string SectionName = "MultiSessionHost";

    public int MaxGlobalParallelSessions { get; init; } = 4;

    public int SchedulerIntervalMs { get; init; } = 500;

    public int HealthLogIntervalMs { get; init; } = 5_000;

    public bool EnableAdminApi { get; init; }

    public string AdminApiUrl { get; init; } = "http://localhost:5088";

    public DriverMode DriverMode { get; init; } = DriverMode.NoOp;

    public bool EnableUiSnapshots { get; init; }

    public BindingStorePersistenceMode BindingStorePersistenceMode { get; init; } = BindingStorePersistenceMode.None;

    public string? BindingStoreFilePath { get; init; }

    public ExecutionCoordinationOptions ExecutionCoordination { get; init; } = new();

    public RiskClassificationOptions RiskClassification { get; init; } = new();

    public PolicyEngineOptions PolicyEngine { get; init; } = new();

    public DecisionExecutionOptions DecisionExecution { get; init; } = new();

    public IReadOnlyList<DesktopTargetProfileOptions> DesktopTargets { get; init; } = [];

    public IReadOnlyList<SessionTargetBindingOptions> SessionTargetBindings { get; init; } = [];

    public IReadOnlyList<SessionDefinitionOptions> Sessions { get; init; } = [];
}

public sealed class DecisionExecutionOptions
{
    public bool EnableDecisionExecution { get; init; } = true;

    public bool AutoExecuteAfterEvaluation { get; init; }

    public int MaxHistoryEntries { get; init; } = 50;

    public int RepeatSuppressionWindowMs { get; init; } = 1_000;

    public bool FailOnUnhandledBlockingDirective { get; init; }

    public bool RecordNoOpExecutions { get; init; } = true;
}

public sealed class SessionDefinitionOptions
{
    public string SessionId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int TickIntervalMs { get; init; } = 1_000;

    public int StartupDelayMs { get; init; } = 250;

    public int MaxParallelWorkItems { get; init; } = 1;

    public int MaxRetryCount { get; init; } = 3;

    public int InitialBackoffMs { get; init; } = 1_000;

    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed class DesktopTargetProfileOptions
{
    public string ProfileName { get; init; } = string.Empty;

    public DesktopTargetKind Kind { get; init; } = DesktopTargetKind.SelfHostedHttpDesktop;

    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitleFragment { get; init; }

    public string? CommandLineFragmentTemplate { get; init; }

    public string? BaseAddressTemplate { get; init; }

    public DesktopSessionMatchingMode MatchingMode { get; init; } = DesktopSessionMatchingMode.WindowTitleAndCommandLine;

    public Dictionary<string, string?> Metadata { get; init; } = [];

    public bool SupportsUiSnapshots { get; init; }

    public bool SupportsStateEndpoint { get; init; }
}

public sealed class SessionTargetBindingOptions
{
    public string SessionId { get; init; } = string.Empty;

    public string TargetProfileName { get; init; } = string.Empty;

    public Dictionary<string, string?> Variables { get; init; } = [];

    public DesktopTargetProfileOverrideOptions? Overrides { get; init; }
}

public sealed class DesktopTargetProfileOverrideOptions
{
    public string? ProcessName { get; init; }

    public string? WindowTitleFragment { get; init; }

    public string? CommandLineFragmentTemplate { get; init; }

    public string? BaseAddressTemplate { get; init; }

    public DesktopSessionMatchingMode? MatchingMode { get; init; }

    public Dictionary<string, string?> Metadata { get; init; } = [];

    public bool? SupportsUiSnapshots { get; init; }

    public bool? SupportsStateEndpoint { get; init; }
}

public sealed class RiskClassificationOptions
{
    public bool EnableRiskClassification { get; init; }

    public IReadOnlyList<RiskRuleOptions> Rules { get; init; } = [];

    public RiskDisposition DefaultUnknownDisposition { get; init; } = RiskDisposition.Unknown;

    public RiskSeverity DefaultUnknownSeverity { get; init; } = RiskSeverity.Unknown;

    public RiskPolicySuggestion DefaultUnknownPolicy { get; init; } = RiskPolicySuggestion.Observe;

    public int MaxReturnedEntities { get; init; } = 100;

    public bool RequireExplicitSafeMatch { get; init; } = true;
}

public sealed class RiskRuleOptions
{
    public string RuleName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> MatchByName { get; init; } = [];

    public RiskRuleMatchMode NameMatchMode { get; init; } = RiskRuleMatchMode.Contains;

    public IReadOnlyList<string> MatchByType { get; init; } = [];

    public RiskRuleMatchMode TypeMatchMode { get; init; } = RiskRuleMatchMode.Exact;

    public IReadOnlyList<string> MatchByTags { get; init; } = [];

    public bool RequireAllTags { get; init; }

    public RiskDisposition Disposition { get; init; } = RiskDisposition.Unknown;

    public RiskSeverity Severity { get; init; } = RiskSeverity.Unknown;

    public int Priority { get; init; }

    public RiskPolicySuggestion SuggestedPolicy { get; init; } = RiskPolicySuggestion.Observe;

    public string Reason { get; init; } = string.Empty;
}

public sealed class PolicyEngineOptions
{
    public bool EnablePolicyEngine { get; init; } = true;

    public IReadOnlyList<string> PolicyOrder { get; init; } =
    [
        "AbortPolicy",
        "ThreatResponsePolicy",
        "TransitPolicy",
        "ResourceUsagePolicy",
        "TargetPrioritizationPolicy",
        "SelectNextSitePolicy"
    ];

    public int MaxReturnedDirectives { get; init; } = 10;

    public bool BlockOnAbort { get; init; } = true;

    public bool PreferThreatResponseOverSelection { get; init; } = true;

    public bool PreferTransitStability { get; init; } = true;

    public int MinDirectivePriority { get; init; }

    public DecisionPlanAggregationRulesOptions AggregationRules { get; init; } = new();

    public AbortPolicyOptions AbortPolicy { get; init; } = new();

    public ThreatResponsePolicyOptions ThreatResponsePolicy { get; init; } = new();

    public TransitPolicyOptions TransitPolicy { get; init; } = new();

    public ResourceUsagePolicyOptions ResourceUsagePolicy { get; init; } = new();

    public TargetPrioritizationPolicyOptions TargetPrioritizationPolicy { get; init; } = new();

    public SelectNextSitePolicyOptions SelectNextSitePolicy { get; init; } = new();

    public BehaviorRulesOptions Rules { get; init; } = new();
}

public sealed class AbortPolicyOptions
{
    public int AbortPriority { get; init; } = 1000;

    public int PausePriority { get; init; } = 950;

    public AbortRulesOptions Rules { get; init; } = new();
}

public sealed class ThreatResponsePolicyOptions
{
    public int WithdrawPriority { get; init; } = 900;

    public int PausePriority { get; init; } = 850;

    public int PrioritizePriority { get; init; } = 760;

    public int AvoidPriority { get; init; } = 740;

    public int ObservePriority { get; init; } = 300;

    public ThreatResponseRulesOptions Rules { get; init; } = new();
}

public sealed class TransitPolicyOptions
{
    public int WaitPriority { get; init; } = 650;

    public int NavigatePriority { get; init; } = 500;

    public int BlockedPriority { get; init; } = 700;

    public TransitRulesOptions Rules { get; init; } = new();
}

public sealed class ResourceUsagePolicyOptions
{
    public int CriticalPriority { get; init; } = 720;

    public int DegradedPriority { get; init; } = 560;

    public double CriticalPercentThreshold { get; init; } = 15;

    public double DegradedPercentThreshold { get; init; } = 35;

    public ResourceUsageRulesOptions Rules { get; init; } = new();
}

public sealed class TargetPrioritizationPolicyOptions
{
    public int PrioritizePriority { get; init; } = 600;

    public int SelectPriority { get; init; } = 520;

    public int AvoidPriority { get; init; } = 540;

    public TargetPrioritizationRulesOptions Rules { get; init; } = new();
}

public sealed class SelectNextSitePolicyOptions
{
    public int SelectSitePriority { get; init; } = 250;

    public int ObservePriority { get; init; } = 150;

    public SiteSelectionRulesOptions Rules { get; init; } = new();
}

public sealed class DecisionPlanAggregationRulesOptions
{
    public IReadOnlyList<DirectiveSuppressionRuleOptions> SuppressionRules { get; init; } =
    [
        new()
        {
            RuleName = "abort-overrides",
            TriggerDirectiveKinds = ["Abort"],
            PreserveDirectiveKinds = ["Abort"],
            SuppressedDirectiveKinds = ["*"]
        },
        new()
        {
            RuleName = "blocking-response-over-selection",
            TriggerDirectiveKinds = ["Withdraw", "PauseActivity"],
            SuppressedDirectiveKinds = ["SelectSite", "Navigate", "SelectTarget"]
        },
        new()
        {
            RuleName = "transit-wait-stability",
            TriggerDirectiveKinds = ["Wait"],
            SuppressedDirectiveKinds = ["SelectSite", "Navigate", "SelectTarget", "PrioritizeTarget", "UseResource"],
            SuppressLowerPriorityOnly = true,
            BlockedByDirectiveKinds = ["Withdraw", "PauseActivity", "AvoidTarget"]
        }
    ];

    public IReadOnlyList<DecisionPlanStatusRuleOptions> StatusRules { get; init; } =
    [
        new()
        {
            RuleName = "aborting-directives",
            Status = "Aborting",
            DirectiveKinds = ["Abort"],
            IncludePolicyAbortFlag = true
        },
        new()
        {
            RuleName = "blocked-directives",
            Status = "Blocked",
            DirectiveKinds = ["Withdraw", "PauseActivity", "Wait"]
        }
    ];
}

public sealed class DirectiveSuppressionRuleOptions
{
    public string RuleName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> TriggerDirectiveKinds { get; init; } = [];

    public IReadOnlyList<string> SuppressedDirectiveKinds { get; init; } = [];

    public IReadOnlyList<string> PreserveDirectiveKinds { get; init; } = [];

    public bool SuppressLowerPriorityOnly { get; init; }

    public IReadOnlyList<string> BlockedByDirectiveKinds { get; init; } = [];
}

public sealed class DecisionPlanStatusRuleOptions
{
    public string RuleName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public string Status { get; init; } = "Ready";

    public IReadOnlyList<string> DirectiveKinds { get; init; } = [];

    public bool IncludePolicyAbortFlag { get; init; }
}

public sealed class BehaviorRulesOptions
{
    public SiteSelectionRulesOptions SiteSelection { get; init; } = new();

    public ThreatResponseRulesOptions ThreatResponse { get; init; } = new();

    public TargetPrioritizationRulesOptions TargetPrioritization { get; init; } = new();

    public ResourceUsageRulesOptions ResourceUsage { get; init; } = new();

    public TransitRulesOptions Transit { get; init; } = new();

    public AbortRulesOptions Abort { get; init; } = new();
}

public sealed class SiteSelectionRulesOptions
{
    public IReadOnlyList<AllowRuleOptions> AllowRules { get; init; } =
    [
        new()
        {
            RuleName = "default-site-selection",
            RequireIdleNavigation = true,
            RequireIdleActivity = true,
            RequireNoActiveTarget = true,
            AllowedThreatSeverities =
            [
                ThreatSeverity.None,
                ThreatSeverity.Low,
                ThreatSeverity.Unknown
            ],
            DirectiveKind = "SelectSite",
            Priority = 250,
            SuggestedPolicy = "SelectSite",
            TargetLabelTemplate = "{siteLabel}",
            Reason = "Configured site selection rule matched."
        }
    ];

    public bool IgnoreNonAllowlistedSites { get; init; } = true;

    public string NoAllowedCandidateDirectiveKind { get; init; } = "Observe";

    public int NoAllowedCandidatePriority { get; init; } = 150;

    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-site-selection-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 150,
        SuggestedPolicy = "Observe",
        Reason = "Configured site-selection fallback matched."
    };

    public int MinimumWaitMs { get; init; }

    public string UnknownSiteLabel { get; init; } = "unknown-worksite";

    public string DefaultSiteLabel { get; init; } = "worksite";
}

public sealed class ThreatResponseRulesOptions
{
    public IReadOnlyList<DenyRuleOptions> DenyRules { get; init; } = [];

    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-threat-response-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 300,
        SuggestedPolicy = "Observe",
        Reason = "Configured threat-response fallback matched."
    };

    public IReadOnlyList<RetreatRuleOptions> RetreatRules { get; init; } =
    [
        new()
        {
            RuleName = "default-withdraw-suggestion",
            MatchSuggestedPolicies = [RiskPolicySuggestion.Withdraw],
            DirectiveKind = "Withdraw",
            Priority = 900,
            SuggestedPolicy = "Withdraw",
            Blocks = true,
            Reason = "Configured withdrawal rule matched."
        },
        new()
        {
            RuleName = "default-critical-threat",
            MinThreatSeverity = ThreatSeverity.Critical,
            DirectiveKind = "Withdraw",
            Priority = 900,
            SuggestedPolicy = "Withdraw",
            Blocks = true,
            Reason = "Configured critical-threat rule matched."
        },
        new()
        {
            RuleName = "default-pause-suggestion",
            MatchSuggestedPolicies = [RiskPolicySuggestion.PauseActivity],
            DirectiveKind = "PauseActivity",
            Priority = 850,
            SuggestedPolicy = "PauseActivity",
            Blocks = true,
            Reason = "Configured pause rule matched."
        },
        new()
        {
            RuleName = "default-prioritize-threat",
            MatchSuggestedPolicies = [RiskPolicySuggestion.Prioritize],
            DirectiveKind = "PrioritizeTarget",
            Priority = 760,
            SuggestedPolicy = "Prioritize",
            Reason = "Configured prioritization rule matched."
        },
        new()
        {
            RuleName = "default-avoid-threat",
            MatchSuggestedPolicies = [RiskPolicySuggestion.Avoid],
            DirectiveKind = "AvoidTarget",
            Priority = 740,
            SuggestedPolicy = "Avoid",
            Reason = "Configured avoid rule matched."
        },
        new()
        {
            RuleName = "default-observe-unknown-threat",
            MinUnknownCount = 1,
            DirectiveKind = "Observe",
            Priority = 300,
            SuggestedPolicy = "Observe",
            Reason = "Configured unknown-threat observation rule matched."
        }
    ];
}

public sealed class TargetPrioritizationRulesOptions
{
    public IReadOnlyList<DenyRuleOptions> DenyRules { get; init; } = [];

    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-target-prioritization-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 150,
        SuggestedPolicy = "Observe",
        Reason = "Configured target-prioritization fallback matched."
    };

    public IReadOnlyList<AllowRuleOptions> PriorityRules { get; init; } =
    [
        new()
        {
            RuleName = "default-risk-prioritize",
            MatchSuggestedPolicies = [RiskPolicySuggestion.Prioritize],
            DirectiveKind = "PrioritizeTarget",
            Priority = 600,
            SuggestedPolicy = "Prioritize",
            Reason = "Configured target priority rule matched."
        },
        new()
        {
            RuleName = "default-risk-avoid",
            MatchSuggestedPolicies =
            [
                RiskPolicySuggestion.Avoid,
                RiskPolicySuggestion.Deprioritize
            ],
            DirectiveKind = "AvoidTarget",
            Priority = 540,
            SuggestedPolicy = "Avoid",
            Reason = "Configured target avoid rule matched."
        },
        new()
        {
            RuleName = "default-active-target",
            RequireActiveTarget = true,
            DirectiveKind = "SelectTarget",
            Priority = 520,
            SuggestedPolicy = "SelectTarget",
            Reason = "Configured active-target rule matched."
        },
        new()
        {
            RuleName = "default-semantic-target",
            MinConfidence = 0,
            DirectiveKind = "SelectTarget",
            Priority = 520,
            SuggestedPolicy = "SelectTarget",
            Reason = "Configured semantic-target rule matched."
        }
    ];
}

public sealed class ResourceUsageRulesOptions
{
    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-resource-usage-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 150,
        SuggestedPolicy = "Observe",
        Reason = "Configured resource-usage fallback matched."
    };

    public IReadOnlyList<AllowRuleOptions> Rules { get; init; } =
    [
        new()
        {
            RuleName = "default-critical-resource",
            MatchResourceCritical = true,
            MaxResourcePercent = 15,
            MaxAvailableCount = 0,
            ThresholdName = "critical-resource",
            DirectiveKind = "Withdraw",
            Priority = 720,
            SuggestedPolicy = "Withdraw",
            Blocks = true,
            Reason = "Configured critical-resource rule matched."
        },
        new()
        {
            RuleName = "default-degraded-resource",
            MatchResourceDegraded = true,
            MaxResourcePercent = 35,
            ThresholdName = "degraded-resource",
            DirectiveKind = "ConserveResource",
            Priority = 560,
            SuggestedPolicy = "ConserveResource",
            Reason = "Configured degraded-resource rule matched."
        },
        new()
        {
            RuleName = "default-defensive-resource-use",
            RequireDefensivePosture = true,
            DirectiveKind = "UseResource",
            Priority = 560,
            SuggestedPolicy = "UseResource",
            Reason = "Configured resource-use rule matched."
        }
    ];
}

public sealed class TransitRulesOptions
{
    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-transit-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 150,
        SuggestedPolicy = "Observe",
        Reason = "Configured transit fallback matched."
    };

    public IReadOnlyList<WaitRuleOptions> Rules { get; init; } =
    [
        new()
        {
            RuleName = "default-transit-blocked",
            MatchNavigationStatuses = [NavigationStatus.Blocked],
            DirectiveKind = "PauseActivity",
            Priority = 700,
            SuggestedPolicy = "PauseActivity",
            Blocks = true,
            Reason = "Configured blocked-transit rule matched."
        },
        new()
        {
            RuleName = "default-transit-in-progress",
            MatchNavigationStatuses = [NavigationStatus.InProgress],
            RequireTransitioning = true,
            DirectiveKind = "Wait",
            Priority = 650,
            SuggestedPolicy = "Wait",
            Blocks = true,
            Reason = "Configured transit-wait rule matched."
        },
        new()
        {
            RuleName = "default-navigation-destination",
            MatchNavigationStatuses = [NavigationStatus.Idle],
            RequireDestination = true,
            DirectiveKind = "Navigate",
            Priority = 500,
            SuggestedPolicy = "Navigate",
            Reason = "Configured navigation rule matched."
        }
    ];
}

public sealed class AbortRulesOptions
{
    public FallbackRuleOptions Fallback { get; init; } = new()
    {
        RuleName = "default-abort-fallback",
        Enabled = false,
        DirectiveKind = "Observe",
        Priority = 150,
        SuggestedPolicy = "Observe",
        Reason = "Configured abort fallback matched."
    };

    public IReadOnlyList<RetreatRuleOptions> Rules { get; init; } =
    [
        new()
        {
            RuleName = "default-runtime-faulted",
            MatchSessionStatuses = [SessionStatus.Faulted],
            DirectiveKind = "Abort",
            Priority = 1000,
            SuggestedPolicy = "Abort",
            Blocks = true,
            Aborts = true,
            Reason = "Configured runtime-fault rule matched."
        },
        new()
        {
            RuleName = "default-critical-domain-withdraw",
            MinThreatSeverity = ThreatSeverity.Critical,
            MatchSuggestedPolicies = [RiskPolicySuggestion.Withdraw],
            DirectiveKind = "Abort",
            Priority = 1000,
            SuggestedPolicy = "Withdraw",
            Blocks = true,
            Aborts = true,
            Reason = "Configured critical domain withdrawal rule matched."
        },
        new()
        {
            RuleName = "default-critical-risk-withdraw",
            MinRiskSeverity = RiskSeverity.Critical,
            MatchSuggestedPolicies = [RiskPolicySuggestion.Withdraw],
            DirectiveKind = "Abort",
            Priority = 1000,
            SuggestedPolicy = "Withdraw",
            Blocks = true,
            Aborts = true,
            Reason = "Configured critical risk withdrawal rule matched."
        },
        new()
        {
            RuleName = "default-resource-warning-pause",
            MatchResourceCritical = true,
            MinWarningCount = 3,
            DirectiveKind = "PauseActivity",
            Priority = 950,
            SuggestedPolicy = "PauseActivity",
            Blocks = true,
            Reason = "Configured resource warning pause rule matched."
        }
    ];
}

public class PolicyRuleOptions
{
    public string RuleName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> MatchLabels { get; init; } = [];

    public PolicyRuleMatchMode LabelMatchMode { get; init; } = PolicyRuleMatchMode.Exact;

    public IReadOnlyList<string> MatchTypes { get; init; } = [];

    public PolicyRuleMatchMode TypeMatchMode { get; init; } = PolicyRuleMatchMode.Exact;

    public IReadOnlyList<string> MatchTags { get; init; } = [];

    public bool RequireAllTags { get; init; }

    public IReadOnlyList<ThreatSeverity> AllowedThreatSeverities { get; init; } = [];

    public ThreatSeverity? MinThreatSeverity { get; init; }

    public RiskSeverity? MinRiskSeverity { get; init; }

    public IReadOnlyList<RiskPolicySuggestion> MatchSuggestedPolicies { get; init; } = [];

    public IReadOnlyList<SessionStatus> MatchSessionStatuses { get; init; } = [];

    public IReadOnlyList<NavigationStatus> MatchNavigationStatuses { get; init; } = [];

    public bool? RequireTransitioning { get; init; }

    public bool RequireDestination { get; init; }

    public bool RequireIdleNavigation { get; init; }

    public bool RequireIdleActivity { get; init; }

    public bool RequireNoActiveTarget { get; init; }

    public bool RequireActiveTarget { get; init; }

    public bool? MatchResourceCritical { get; init; }

    public bool? MatchResourceDegraded { get; init; }

    public bool RequireDefensivePosture { get; init; }

    public double? MinProgressPercent { get; init; }

    public double? MaxProgressPercent { get; init; }

    public double? MinResourcePercent { get; init; }

    public double? MaxResourcePercent { get; init; }

    public int? MinWarningCount { get; init; }

    public int? MaxWarningCount { get; init; }

    public int? MinUnknownCount { get; init; }

    public int? MaxUnknownCount { get; init; }

    public int? MinAvailableCount { get; init; }

    public int? MaxAvailableCount { get; init; }

    public double? MinConfidence { get; init; }

    public double? MaxConfidence { get; init; }

    public string? MetricName { get; init; }

    public double? MinMetricValue { get; init; }

    public double? MaxMetricValue { get; init; }

    public string DirectiveKind { get; init; } = "Observe";

    public int Priority { get; init; }

    public string SuggestedPolicy { get; init; } = "Observe";

    public bool Blocks { get; init; }

    public bool Aborts { get; init; }

    public int MinimumWaitMs { get; init; }

    public string? ThresholdName { get; init; }

    public string? PolicyMode { get; init; }

    public string? TargetLabelTemplate { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed class AllowRuleOptions : PolicyRuleOptions;

public sealed class DenyRuleOptions : PolicyRuleOptions;

public sealed class RetreatRuleOptions : PolicyRuleOptions;

public sealed class WaitRuleOptions : PolicyRuleOptions;

public sealed class FallbackRuleOptions : PolicyRuleOptions;
