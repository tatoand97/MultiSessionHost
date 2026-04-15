using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// Default implementation of activity state evaluation.
/// Applies deterministic state transition rules based on domain, risk, and decision plan signals.
/// </summary>
public sealed class DefaultSessionActivityStateEvaluator : ISessionActivityStateEvaluator
{
    private readonly IObservabilityRecorder _observabilityRecorder;

    public DefaultSessionActivityStateEvaluator(IObservabilityRecorder observabilityRecorder)
    {
        _observabilityRecorder = observabilityRecorder;
    }

    public ValueTask<SessionActivityEvaluationResult> EvaluateAsync(
        SessionActivityEvaluationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTimeOffset.UtcNow;

        var previousSnapshot = context.PreviousSnapshot ?? SessionActivitySnapshot.CreateBootstrap(context.SessionId, context.EvaluatedAtUtc);
        var newState = EvaluateState(context, previousSnapshot.CurrentState);

        if (newState == previousSnapshot.CurrentState)
        {
            // No state change
            return ValueTask.FromResult(new SessionActivityEvaluationResult(
                previousSnapshot,
                Transition: null,
                EvaluationReasonCode: "no-state-change",
                EvaluationReason: "Current state remains unchanged"));
        }

        var transition = new SessionActivityTransition(
            FromState: previousSnapshot.CurrentState,
            ToState: newState,
            ReasonCode: GetReasonCode(context, newState),
            Reason: GetReason(context, newState),
            OccurredAtUtc: context.EvaluatedAtUtc,
            Metadata: GetTransitionMetadata(context, newState));

        var updatedSnapshot = InMemorySessionActivityStateStore.AppendTransition(previousSnapshot, transition);

        var result = new SessionActivityEvaluationResult(
            updatedSnapshot,
            transition,
            EvaluationReasonCode: GetReasonCode(context, newState),
            EvaluationReason: GetReason(context, newState));

        _ = _observabilityRecorder.RecordActivityAsync(
            context.SessionId,
            "activity.state",
            MapOutcome(newState),
            DateTimeOffset.UtcNow - startedAt,
            result.EvaluationReasonCode,
            result.EvaluationReason,
            nameof(DefaultSessionActivityStateEvaluator),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["state"] = newState.ToString(),
                ["previousState"] = previousSnapshot.CurrentState.ToString()
            },
            cancellationToken);

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Determines the next state based on signals, following explicit precedence order.
    /// </summary>
    private static SessionActivityStateKind EvaluateState(
        SessionActivityEvaluationContext context,
        SessionActivityStateKind currentState)
    {
        // 1. Faulted: runtime/domain error condition
        if (IsFaulted(context))
            return SessionActivityStateKind.Faulted;

        // 2. Withdrawing: explicit Withdraw directive or critical risk
        if (IsWithdrawing(context))
            return SessionActivityStateKind.Withdrawing;

        // 3. Hiding: PauseActivity directive
        if (IsHiding(context))
            return SessionActivityStateKind.Hiding;

        // 4. Recovering: recovery path from prior blocking
        if (IsRecovering(context, currentState))
            return SessionActivityStateKind.Recovering;

        // 5. Traveling: navigation in active progress
        if (IsTraveling(context))
            return SessionActivityStateKind.Traveling;

        // 6. Arriving: navigation just completed
        if (IsArriving(context))
            return SessionActivityStateKind.Arriving;

        // 7. WaitingForSpawn: at location, no engagement, plan waiting
        if (IsWaitingForSpawn(context))
            return SessionActivityStateKind.WaitingForSpawn;

        // 8. Engaging: active combat/target engagement
        if (IsEngaging(context))
            return SessionActivityStateKind.Engaging;

        // 9. MonitoringRisk: ongoing activity with risk
        if (IsMonitoringRisk(context))
            return SessionActivityStateKind.MonitoringRisk;

        // 10. SelectingWorksite: site selection phase
        if (IsSelectingWorksite(context))
            return SessionActivityStateKind.SelectingWorksite;

        // 11. Idle: fallback, no stronger signal
        return SessionActivityStateKind.Idle;
    }

    private static bool IsFaulted(SessionActivityEvaluationContext context)
    {
        // Check domain state warnings or error conditions
        if (context.DomainState.Warnings.Any(w => w.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                                  w.Contains("fault", StringComparison.OrdinalIgnoreCase) ||
                                                  w.Contains("critical", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Could check navigation error state, combat crash, etc.
        return false;
    }

    private static bool IsWithdrawing(SessionActivityEvaluationContext context)
    {
        // Check for explicit Withdraw directive
        if (context.DecisionPlan.Directives.Any(d => d.DirectiveKind == DecisionDirectiveKind.Withdraw))
            return true;

        // Check for high-risk withdrawal policy signal
        if (context.RiskAssessment?.Summary.HasWithdrawPolicy == true)
            return true;

        return false;
    }

    private static bool IsHiding(SessionActivityEvaluationContext context)
    {
        // Check for PauseActivity directive
        return context.DecisionPlan.Directives.Any(d => d.DirectiveKind == DecisionDirectiveKind.PauseActivity);
    }

    private static bool IsRecovering(SessionActivityEvaluationContext context, SessionActivityStateKind currentState)
    {
        // Recovering applies when transitioning out of Withdrawing/Hiding/degraded states with recovery signals
        if (currentState != SessionActivityStateKind.Withdrawing && 
            currentState != SessionActivityStateKind.Hiding && 
            currentState != SessionActivityStateKind.Faulted)
            return false;

        // Recovery indicated by resource improvement, threat reduction, or explicit recovery directives
        var resourcesImproving = context.DomainState.Resources.HealthPercent.HasValue && 
                               context.DomainState.Resources.HealthPercent > 0.3;
        var threatReducing = context.RiskAssessment?.Summary.ThreatCount < 3;
        var recoverySignals = !context.DecisionPlan.Directives.Any(d => 
            d.DirectiveKind == DecisionDirectiveKind.Withdraw ||
            d.DirectiveKind == DecisionDirectiveKind.PauseActivity);

        return resourcesImproving && threatReducing && recoverySignals;
    }

    private static bool IsTraveling(SessionActivityEvaluationContext context)
    {
        // Navigation is in active progress
        return context.DomainState.Navigation.Status == NavigationStatus.InProgress && 
               !context.DomainState.Navigation.IsTransitioning;
    }

    private static bool IsArriving(SessionActivityEvaluationContext context)
    {
        // Navigation just completed or arrival context change detected
        return context.DomainState.Navigation.Status != NavigationStatus.InProgress && 
               context.DomainState.Navigation.IsTransitioning;
    }

    private static bool IsWaitingForSpawn(SessionActivityEvaluationContext context)
    {
        // At location, no engagement, plan is waiting/observe-oriented
        if (context.DomainState.Navigation.Status == NavigationStatus.InProgress)
            return false;

        if (context.DomainState.Combat.Status == CombatStatus.Engaged)
            return false;

        if (context.DomainState.Target.HasActiveTarget)
            return false;

        // Plan has observe/wait directives but no engage directives
        var hasWaitDirective = context.DecisionPlan.Directives.Any(d =>
            d.DirectiveKind == DecisionDirectiveKind.Observe ||
            d.DirectiveKind == DecisionDirectiveKind.Wait);

        return hasWaitDirective;
    }

    private static bool IsEngaging(SessionActivityEvaluationContext context)
    {
        // Combat active or target engagement underway
        return context.DomainState.Combat.Status == CombatStatus.Engaged || 
               context.DomainState.Target.HasActiveTarget;
    }

    private static bool IsMonitoringRisk(SessionActivityEvaluationContext context)
    {
        // Activity ongoing, risk present, no stronger state applies
        var hasActivity = context.DecisionPlan.PlanStatus == DecisionPlanStatus.Ready ||
                         context.DecisionPlan.Directives.Any(d => d.Priority > 0);

        var hasRisk = context.RiskAssessment?.Summary.ThreatCount > 0 ||
                     context.RiskAssessment?.Summary.HighestSeverity != RiskSeverity.Unknown;

        return hasActivity && hasRisk;
    }

    private static bool IsSelectingWorksite(SessionActivityEvaluationContext context)
    {
        // Plan indicates site selection or navigate-toward-work
        return context.DecisionPlan.Directives.Any(d =>
            d.DirectiveKind == DecisionDirectiveKind.SelectSite ||
            (d.DirectiveKind == DecisionDirectiveKind.Navigate && 
             d.SourcePolicy?.Contains("SelectNextSite", StringComparison.OrdinalIgnoreCase) == true));
    }

    private static string GetReasonCode(SessionActivityEvaluationContext context, SessionActivityStateKind newState) =>
        newState switch
        {
            SessionActivityStateKind.Faulted => "domain-fault-detected",
            SessionActivityStateKind.Withdrawing => context.DecisionPlan.Directives.Any(d => d.DirectiveKind == DecisionDirectiveKind.Withdraw) 
                ? "decision-plan-withdraw" 
                : "risk-policy-withdraw",
            SessionActivityStateKind.Hiding => "decision-plan-pause-activity",
            SessionActivityStateKind.Recovering => "recovery-path-active",
            SessionActivityStateKind.Traveling => "navigation-in-progress",
            SessionActivityStateKind.Arriving => "navigation-arrival-detected",
            SessionActivityStateKind.WaitingForSpawn => "awaiting-target-spawn",
            SessionActivityStateKind.Engaging => context.DomainState.Combat.Status == CombatStatus.Engaged ? "combat-engaged" : "target-selected",
            SessionActivityStateKind.MonitoringRisk => "monitoring-risk-after-activity",
            SessionActivityStateKind.SelectingWorksite => "selecting-worksite",
            _ => "idle-fallback"
        };

    private static string GetReason(SessionActivityEvaluationContext context, SessionActivityStateKind newState) =>
        newState switch
        {
            SessionActivityStateKind.Faulted => "Domain has encountered a fault or critical error",
            SessionActivityStateKind.Withdrawing => context.DecisionPlan.Directives.Any(d => d.DirectiveKind == DecisionDirectiveKind.Withdraw)
                ? "Decision plan contains Withdraw directive"
                : "Risk policy indicates withdrawal required",
            SessionActivityStateKind.Hiding => "Decision plan contains PauseActivity directive for safety",
            SessionActivityStateKind.Recovering => "Session is recovering from prior blocking or degraded state",
            SessionActivityStateKind.Traveling => "Navigation is in active progress toward destination",
            SessionActivityStateKind.Arriving => "Navigation has completed; session has arrived",
            SessionActivityStateKind.WaitingForSpawn => "At location awaiting target spawn or engagement opportunity",
            SessionActivityStateKind.Engaging => context.DomainState.Combat.Status == CombatStatus.Engaged ? "Combat engagement is active" : "Target has been selected for engagement",
            SessionActivityStateKind.MonitoringRisk => "Activity ongoing with risk present but no stronger state applies",
            SessionActivityStateKind.SelectingWorksite => "Session is in worksite selection phase",
            _ => "No actionable directives or signals; session is idle"
        };

    private static IReadOnlyDictionary<string, string> GetTransitionMetadata(
        SessionActivityEvaluationContext context,
        SessionActivityStateKind newState)
    {
        var metadata = new Dictionary<string, string>();

        // Add relevant domain context
        if (context.DomainState.Navigation.DestinationLabel is not null)
            metadata["destination"] = context.DomainState.Navigation.DestinationLabel;

        if (context.DomainState.Target.PrimaryTargetId is not null)
            metadata["active_target"] = context.DomainState.Target.PrimaryTargetId;

        if (context.DomainState.Combat.Status == CombatStatus.Engaged)
            metadata["combat_engaged"] = "true";

        // Add resource context
        if (context.DomainState.Resources.HealthPercent is not null)
            metadata["health_percent"] = context.DomainState.Resources.HealthPercent.Value.ToString("F2");

        // Add risk context
        if (context.RiskAssessment is not null)
        {
            metadata["threat_count"] = context.RiskAssessment.Summary.ThreatCount.ToString();
            metadata["highest_severity"] = context.RiskAssessment.Summary.HighestSeverity.ToString();
            if (context.RiskAssessment.Summary.TopCandidateId is not null)
                metadata["top_risk"] = context.RiskAssessment.Summary.TopCandidateId;
        }

        // Add directive count
        metadata["directive_count"] = context.DecisionPlan.Directives.Count.ToString();
        metadata["plan_status"] = context.DecisionPlan.PlanStatus.ToString();

        return metadata;
    }

    private static string MapOutcome(SessionActivityStateKind state) =>
        state switch
        {
            SessionActivityStateKind.Withdrawing => SessionObservabilityOutcome.Withdrawn.ToString(),
            SessionActivityStateKind.Hiding => SessionObservabilityOutcome.Hidden.ToString(),
            SessionActivityStateKind.WaitingForSpawn => SessionObservabilityOutcome.Waiting.ToString(),
            SessionActivityStateKind.Faulted => SessionObservabilityOutcome.Aborted.ToString(),
            _ => SessionObservabilityOutcome.Success.ToString()
        };
}
