using System.Globalization;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class EveLikeTravelAutopilotBehaviorPack : ITargetBehaviorPack
{
    public const string BehaviorPackName = "EveLikeTravelAutopilot";
    public const string BehaviorPackVersion = "7.1.0";

    private readonly ITravelAutopilotActionSelector _actionSelector;
    private readonly SessionHostOptions _options;

    public EveLikeTravelAutopilotBehaviorPack(
        SessionHostOptions options,
        ITravelAutopilotActionSelector actionSelector)
    {
        _options = options;
        _actionSelector = actionSelector;
    }

    public string PackName => BehaviorPackName;

    public string PackVersion => BehaviorPackVersion;

    public ValueTask<TargetBehaviorPlanningResult> PlanAsync(TargetBehaviorPlanningContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        var reasonCodes = new List<string>();
        var reasonMessages = new List<string>();
        var memoryState = TravelAutopilotMemoryState.FromMetadata(context.OperationalMemorySnapshot?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
        memoryState = memoryState with { BehaviorPackName = PackName };

        var package = context.SemanticExtraction?.Packages.FirstOrDefault(static item => item.EveLike is not null)?.EveLike;
        if (package is null)
        {
            reasonCodes.Add(BehaviorReasonCodes.NoBehaviorPack);
            reasonMessages.Add("The selected target behavior pack is unavailable because the EveLike semantic package did not produce output.");
            return ValueTask.FromResult(BuildPlan(context, packageName: PackName, TargetBehaviorPlanningStateKind.StaleSemanticState, TravelAutopilotActionIntent.RefreshUi, null, null, memoryState, warnings, reasonCodes, reasonMessages));
        }

        var route = package.TravelRoute;
        var routeFingerprint = ComputeRouteFingerprint(package);
        var observabilityInsufficient = IsObservabilityInsufficient(context, package);
        var routeChanged = !string.Equals(memoryState.RouteFingerprint, routeFingerprint, StringComparison.Ordinal);
        var progressChanged = route.ProgressPercent is not null && !AreClose(route.ProgressPercent, memoryState.LastObservedProgressPercent);
        var transitionHints = context.SessionDomainState.Navigation.IsTransitioning || context.ActivitySnapshot?.CurrentState is SessionActivityStateKind.Traveling or SessionActivityStateKind.Arriving;
        var policyPaused = context.PolicyControlState.IsPolicyPaused;
        var policyBlocked = IsPolicyBlocked(context.CurrentDecisionPlan);
        var recoveryBlocked = IsRecoveryBlocked(context.RecoverySnapshot);
        var recoveryRequiresRefresh = IsRecoveryRefreshRequired(context.RecoverySnapshot);
        var riskBlocked = IsRiskBlocked(context.RiskAssessment, package);
        var isArrived = IsArrived(context, package, memoryState);

        if (policyPaused || policyBlocked)
        {
            reasonCodes.Add(BehaviorReasonCodes.BlockedPolicy);
            reasonMessages.Add(policyPaused ? "Policy control is paused; travel progression remains blocked." : "The current policy decision plan blocks travel progression.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.BlockedByPolicy, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastOutcomeCode = BehaviorReasonCodes.BlockedPolicy }, warnings, reasonCodes, reasonMessages));
        }

        if (recoveryBlocked)
        {
            reasonCodes.Add(BehaviorReasonCodes.BlockedRecovery);
            reasonMessages.Add("Recovery state is blocking travel progression.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.BlockedByRecovery, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastOutcomeCode = BehaviorReasonCodes.BlockedRecovery }, warnings, reasonCodes, reasonMessages));
        }

        if (observabilityInsufficient)
        {
            reasonCodes.Add(BehaviorReasonCodes.ObservabilityInsufficient);
            reasonMessages.Add("Native target observability is insufficient (UIA root-only), so travel progression is blocked until a richer observation is available.");
            warnings.Add("Behavior planning paused because semantic state was derived from an opaque root-only native target.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.ObservabilityInsufficient, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastOutcomeCode = BehaviorReasonCodes.ObservabilityInsufficient }, warnings, reasonCodes, reasonMessages));
        }

        if (isArrived)
        {
            reasonCodes.Add(BehaviorReasonCodes.Arrived);
            reasonMessages.Add("The route appears complete or the destination was reached.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.Arrived, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastArrivalDetectedAtUtc = context.Now, LastOutcomeCode = BehaviorReasonCodes.Arrived }, warnings, reasonCodes, reasonMessages));
        }

        if (!route.RouteActive && string.IsNullOrWhiteSpace(route.DestinationLabel) && string.IsNullOrWhiteSpace(route.NextWaypointLabel))
        {
            reasonCodes.Add(BehaviorReasonCodes.NoRoute);
            reasonMessages.Add("No active travel route was available.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.NoRoute, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastOutcomeCode = BehaviorReasonCodes.NoRoute }, warnings, reasonCodes, reasonMessages));
        }

        if (riskBlocked)
        {
            reasonCodes.Add(BehaviorReasonCodes.BlockedRisk);
            reasonMessages.Add("Unsafe or high-threat context blocks travel progression.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.BlockedByRisk, TravelAutopilotActionIntent.None, null, null, memoryState with { RouteFingerprint = routeFingerprint, LastOutcomeCode = BehaviorReasonCodes.BlockedRisk }, warnings, reasonCodes, reasonMessages));
        }

        if (recoveryRequiresRefresh || context.SessionUiState?.ProjectedTree is null)
        {
            reasonCodes.Add(BehaviorReasonCodes.RefreshRequired);
            reasonMessages.Add("The snapshot is stale or the projected UI tree is unavailable; a refresh is required.");
            var refreshSelection = _actionSelector.SelectAction(context, package, memoryState with { RouteFingerprint = routeFingerprint }, TravelAutopilotActionIntent.RefreshUi)
                ?? throw new InvalidOperationException("Refresh UI selection could not be resolved.");

            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.RefreshRequired, TravelAutopilotActionIntent.RefreshUi, package, refreshSelection, memoryState with { RouteFingerprint = routeFingerprint, LastObservedProgressPercent = route.ProgressPercent, LastOutcomeCode = BehaviorReasonCodes.RefreshRequired }, warnings, reasonCodes, reasonMessages));
        }

        if (transitionHints || (memoryState.LastActionAtUtc is not null && context.Now - memoryState.LastActionAtUtc.Value < TimeSpan.FromMilliseconds(_options.DecisionExecution.RepeatSuppressionWindowMs)))
        {
            reasonCodes.Add(BehaviorReasonCodes.AwaitingTransition);
            reasonMessages.Add("Travel is already transitioning or the last travel action was issued too recently.");
            var waitPlan = BuildPlan(
                context,
                PackName,
                TargetBehaviorPlanningStateKind.AwaitingTravelTransition,
                TravelAutopilotActionIntent.None,
                null,
                null,
                memoryState with
                {
                    RouteFingerprint = routeFingerprint,
                    LastDestinationLabel = route.DestinationLabel,
                    LastCurrentLocationLabel = route.CurrentLocationLabel,
                    LastNextWaypointLabel = route.NextWaypointLabel,
                    LastObservedProgressPercent = route.ProgressPercent,
                    UnchangedRouteTickCount = routeChanged || progressChanged ? 0 : memoryState.UnchangedRouteTickCount + 1,
                    LastOutcomeCode = BehaviorReasonCodes.AwaitingTransition
                },
                warnings,
                reasonCodes,
                reasonMessages);

            return ValueTask.FromResult(waitPlan);
        }

        var actionIntent = route.RouteActive
            ? route.NextWaypointLabel is not null
                ? TravelAutopilotActionIntent.SelectWaypoint
                : TravelAutopilotActionIntent.ToggleAutopilot
            : TravelAutopilotActionIntent.RefreshUi;

        var actionSelection = _actionSelector.SelectAction(context, package, memoryState with { RouteFingerprint = routeFingerprint }, actionIntent);
        if (actionSelection is null)
        {
            reasonCodes.Add(BehaviorReasonCodes.AwaitingProgress);
            reasonMessages.Add("Travel is active but no progress action could be resolved from the projected UI tree.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.AwaitingRouteProgress, TravelAutopilotActionIntent.None, package, null, memoryState with { RouteFingerprint = routeFingerprint, LastObservedProgressPercent = route.ProgressPercent, UnchangedRouteTickCount = memoryState.UnchangedRouteTickCount + 1, LastOutcomeCode = BehaviorReasonCodes.AwaitingProgress }, warnings, reasonCodes, reasonMessages));
        }

        var suppressedForSpam = memoryState.RouteFingerprint is not null &&
            string.Equals(memoryState.RouteFingerprint, routeFingerprint, StringComparison.Ordinal) &&
            string.Equals(memoryState.LastActionCode, actionSelection.ActionCode, StringComparison.OrdinalIgnoreCase) &&
            memoryState.LastActionAtUtc is not null &&
            context.Now - memoryState.LastActionAtUtc.Value < TimeSpan.FromMilliseconds(_options.DecisionExecution.RepeatSuppressionWindowMs);

        if (suppressedForSpam)
        {
            reasonCodes.Add(BehaviorReasonCodes.AwaitingTransition);
            reasonMessages.Add("The same travel action was recently issued for the current route state, so it was suppressed.");
            return ValueTask.FromResult(BuildPlan(context, PackName, TargetBehaviorPlanningStateKind.AwaitingRouteProgress, TravelAutopilotActionIntent.None, package, null, memoryState with { RouteFingerprint = routeFingerprint, LastObservedProgressPercent = route.ProgressPercent, UnchangedRouteTickCount = memoryState.UnchangedRouteTickCount + 1, LastOutcomeCode = BehaviorReasonCodes.AwaitingTransition }, warnings, reasonCodes, reasonMessages));
        }

        reasonCodes.Add(actionSelection.ReasonCode);
        reasonMessages.Add(actionSelection.Reason);
        return ValueTask.FromResult(BuildPlan(context, PackName, route.RouteActive ? TargetBehaviorPlanningStateKind.RouteReady : TargetBehaviorPlanningStateKind.StaleSemanticState, actionSelection.Intent, package, actionSelection, memoryState with
        {
            RouteFingerprint = routeFingerprint,
            LastDestinationLabel = route.DestinationLabel,
            LastCurrentLocationLabel = route.CurrentLocationLabel,
            LastNextWaypointLabel = route.NextWaypointLabel,
            LastActionCode = actionSelection.ActionCode,
            LastActionAtUtc = context.Now,
            LastObservedProgressPercent = route.ProgressPercent,
            UnchangedRouteTickCount = routeChanged || progressChanged ? 0 : memoryState.UnchangedRouteTickCount + 1,
            LastOutcomeCode = actionSelection.ReasonCode
        }, warnings, reasonCodes, reasonMessages));
    }

    private TargetBehaviorPlanningResult BuildPlan(
        TargetBehaviorPlanningContext context,
        string packageName,
        TargetBehaviorPlanningStateKind stateKind,
        TravelAutopilotActionIntent actionIntent,
        EveLikeSemanticPackageResult? package,
        TravelAutopilotActionSelection? selection,
        TravelAutopilotMemoryState memoryState,
        ICollection<string> warnings,
        IReadOnlyList<string> reasonCodes,
        IReadOnlyList<string> reasonMessages)
    {
        var route = package?.TravelRoute;
        var routeFingerprint = package is null ? memoryState.RouteFingerprint ?? string.Empty : ComputeRouteFingerprint(package);
        var reasonCode = reasonCodes.FirstOrDefault() ?? BehaviorReasonCodes.NoBehaviorPack;
        var reason = reasonMessages.FirstOrDefault() ?? "Travel behavior planning did not produce a progress action.";
        var status = stateKind is TargetBehaviorPlanningStateKind.BlockedByPolicy or TargetBehaviorPlanningStateKind.BlockedByRecovery or TargetBehaviorPlanningStateKind.BlockedByRisk
            ? DecisionPlanStatus.Blocked
            : selection?.Intent is TravelAutopilotActionIntent.None or null
                ? DecisionPlanStatus.Idle
                : DecisionPlanStatus.Ready;

        var directiveMetadata = selection is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(selection.Metadata, StringComparer.Ordinal)
            {
                ["behaviorStateKind"] = stateKind.ToString(),
                ["travelActionIntent"] = actionIntent.ToString(),
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
                ["routeFingerprint"] = routeFingerprint
            };

        List<DecisionDirective> directives = selection is null
            ? []
            : [new DecisionDirective(
                $"travel-{selection.Intent.ToString().ToLowerInvariant()}-{context.Now:yyyyMMddHHmmssfff}",
                DecisionDirectiveKind.Navigate,
                600,
                PackName,
                selection.Command.NodeId?.Value,
                selection.Command.NodeId?.Value,
                PackName,
                directiveMetadata,
                [new DecisionReason(PackName, reasonCode, reason, directiveMetadata)])];

        if (selection is null && stateKind is TargetBehaviorPlanningStateKind.AwaitingTravelTransition)
        {
            directives =
            [
                new DecisionDirective(
                    $"travel-wait-{context.Now:yyyyMMddHHmmssfff}",
                    DecisionDirectiveKind.Wait,
                    550,
                    PackName,
                    null,
                    null,
                    PackName,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["behaviorStateKind"] = stateKind.ToString(),
                        ["reasonCode"] = reasonCode,
                        ["reason"] = reason,
                        ["notBeforeUtc"] = context.Now.AddMilliseconds(_options.DecisionExecution.RepeatSuppressionWindowMs).ToString("O")
                    },
                    [new DecisionReason(PackName, reasonCode, reason, new Dictionary<string, string>(StringComparer.Ordinal))])
            ];
        }

        var planReasons = reasonCodes.Zip(reasonMessages, (code, message) => new DecisionReason(PackName, code, message, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["behaviorPack"] = PackName,
            ["stateKind"] = stateKind.ToString(),
            ["routeFingerprint"] = routeFingerprint
        })).ToArray();

        var decisionPlan = new DecisionPlan(
            context.SessionSnapshot.SessionId,
            context.Now,
            status,
            directives,
            planReasons,
            new PolicyExecutionSummary([PackName], planReasons.Length > 0 ? [PackName] : [], [], [], directives.Count, directives.Count, new Dictionary<string, int>(StringComparer.Ordinal)),
            warnings.ToArray(),
            new DecisionPlanExplanation([], [], directives.Select(static directive => directive.DirectiveKind.ToString()).ToArray(), warnings.ToArray(), planReasons.Select(static reason => reason.Code).ToArray()));

        var state = new TargetBehaviorPlanningState(
            PackName,
            stateKind,
            routeFingerprint,
            route?.RouteActive == true,
            context.SessionDomainState.Navigation.IsTransitioning,
            stateKind == TargetBehaviorPlanningStateKind.Arrived,
            context.PolicyControlState.IsPolicyPaused,
            stateKind == TargetBehaviorPlanningStateKind.BlockedByRecovery,
            stateKind == TargetBehaviorPlanningStateKind.BlockedByRisk,
            route?.DestinationLabel,
            route?.CurrentLocationLabel,
            route?.NextWaypointLabel,
            route?.WaypointCount ?? 0,
            route?.ProgressPercent,
            actionIntent,
            selection?.ActionCode,
            reasonCode,
            reason);

        var memoryMetadata = memoryState with
        {
            BehaviorPackName = PackName,
            RouteFingerprint = routeFingerprint,
            LastDestinationLabel = route?.DestinationLabel,
            LastCurrentLocationLabel = route?.CurrentLocationLabel,
            LastNextWaypointLabel = route?.NextWaypointLabel,
            LastObservedProgressPercent = route?.ProgressPercent,
            LastActionCode = selection?.ActionCode ?? memoryState.LastActionCode,
            LastActionAtUtc = selection is null ? memoryState.LastActionAtUtc : context.Now,
            UnchangedRouteTickCount = stateKind is TargetBehaviorPlanningStateKind.AwaitingTravelTransition or TargetBehaviorPlanningStateKind.BlockedByPolicy or TargetBehaviorPlanningStateKind.BlockedByRecovery or TargetBehaviorPlanningStateKind.BlockedByRisk
                ? memoryState.UnchangedRouteTickCount + 1
                : 0,
            LastArrivalDetectedAtUtc = stateKind == TargetBehaviorPlanningStateKind.Arrived ? context.Now : memoryState.LastArrivalDetectedAtUtc,
            LastOutcomeCode = reasonCode
        };

        return new TargetBehaviorPlanningResult(PackName, status.ToString(), reasonCode, reason, decisionPlan, state, memoryMetadata, warnings.ToArray(), directiveMetadata);
    }

    private static bool IsObservabilityInsufficient(TargetBehaviorPlanningContext context, EveLikeSemanticPackageResult package)
    {
        var metadata = context.SessionUiState?.ProjectedTree?.Metadata.Properties;
        if (metadata is not null &&
            metadata.TryGetValue("opaqueRoot", out var opaqueRootValue) &&
            bool.TryParse(opaqueRootValue, out var opaqueRoot) &&
            opaqueRoot)
        {
            return true;
        }

        if (metadata is not null &&
            metadata.TryGetValue("observabilityMode", out var mode) &&
            string.Equals(mode, "RootOnly", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return package.Warnings.Any(static warning => warning.Contains("insufficient observable structure", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRecoveryBlocked(SessionRecoverySnapshot recoverySnapshot) =>
        recoverySnapshot.RecoveryStatus is SessionRecoveryStatus.CircuitOpen or SessionRecoveryStatus.Backoff or SessionRecoveryStatus.Quarantined or SessionRecoveryStatus.Exhausted or SessionRecoveryStatus.Faulted ||
        recoverySnapshot.IsAttachmentInvalid ||
        recoverySnapshot.IsTargetQuarantined;

    private static bool IsRecoveryRefreshRequired(SessionRecoverySnapshot recoverySnapshot) =>
        recoverySnapshot.IsSnapshotStale || recoverySnapshot.MetadataDriftDetected;

    private static bool IsPolicyBlocked(DecisionPlan? decisionPlan)
    {
        if (decisionPlan is null)
        {
            return false;
        }

        if (decisionPlan.PlanStatus == DecisionPlanStatus.Aborting)
        {
            return true;
        }

        return decisionPlan.Directives.Any(static directive => directive.DirectiveKind is DecisionDirectiveKind.Abort or DecisionDirectiveKind.Withdraw or DecisionDirectiveKind.PauseActivity);
    }

    private static bool IsRiskBlocked(RiskAssessmentResult? riskAssessment, EveLikeSemanticPackageResult package)
    {
        if (riskAssessment is null)
        {
            return package.Tactical.HostileCandidateCount > 0 || package.Safety.IsSafeLocation is false;
        }

        return riskAssessment.Summary.HighestSeverity is RiskSeverity.High or RiskSeverity.Critical ||
               riskAssessment.Summary.ThreatCount > 0 ||
               riskAssessment.Summary.UnknownCount > 0 && !package.Safety.IsSafeLocation;
    }

    private static bool IsArrived(TargetBehaviorPlanningContext context, EveLikeSemanticPackageResult package, TravelAutopilotMemoryState memoryState)
    {
        if (package.TravelRoute.RouteActive)
        {
            return false;
        }

        if (package.TravelRoute.ProgressPercent is >= 99.5)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(package.TravelRoute.DestinationLabel) &&
            string.Equals(package.TravelRoute.DestinationLabel, package.TravelRoute.CurrentLocationLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return memoryState.LastArrivalDetectedAtUtc is not null &&
               string.Equals(memoryState.RouteFingerprint, ComputeRouteFingerprint(package), StringComparison.Ordinal);
    }

    private static string ComputeRouteFingerprint(EveLikeSemanticPackageResult package) =>
        string.Join('|',
            package.PackageName,
            package.TravelRoute.RouteActive,
            package.TravelRoute.DestinationLabel ?? string.Empty,
            package.TravelRoute.CurrentLocationLabel ?? string.Empty,
            package.TravelRoute.NextWaypointLabel ?? string.Empty,
            package.TravelRoute.WaypointCount.ToString(CultureInfo.InvariantCulture),
            package.TravelRoute.ProgressPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool AreClose(double? left, double? right) =>
        left is null && right is null ||
        left is not null && right is not null && Math.Abs(left.Value - right.Value) < 0.01d;
}
