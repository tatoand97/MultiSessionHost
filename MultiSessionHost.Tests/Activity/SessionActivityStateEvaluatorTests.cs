using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Recovery;

namespace MultiSessionHost.Tests.Activity;

public class SessionActivityStateEvaluatorTests
{
    private readonly DefaultSessionActivityStateEvaluator _evaluator = new(new NoOpObservabilityRecorder());
    private readonly DateTimeOffset _testNow = DateTimeOffset.UtcNow;

    [Fact]
    public async Task EvaluateAsync_BootstrapSnapshot_InitializesInIdleState()
    {
        var sessionId = new SessionId("test-1");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, null, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Idle, result.NewSnapshot.CurrentState);
        // On bootstrap, it creates a bootstrap snapshot internally and evaluates.
        // Since both are Idle, there's no transition
        Assert.Null(result.Transition);
    }

    [Fact]
    public async Task EvaluateAsync_NoStateChange_ReturnsNoTransition()
    {
        var sessionId = new SessionId("test-2");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, previousSnapshot, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Idle, result.NewSnapshot.CurrentState);
        Assert.Null(result.Transition);
        Assert.Equal("no-state-change", result.EvaluationReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_WithWithdrawDirective_TransitionsToWithdrawing()
    {
        var sessionId = new SessionId("test-3");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var metadata = new Dictionary<string, string>();
        var directive = new DecisionDirective(
            "withdraw-1",
            DecisionDirectiveKind.Withdraw,
            100,
            "ThreatResponsePolicy",
            null, null, null,
            metadata,
            []);
        var plan = new DecisionPlan(
            sessionId, _testNow, DecisionPlanStatus.Ready,
            [directive], [],
            new PolicyExecutionSummary([], [], [], [], 1, 1, new Dictionary<string, int>()),
            []);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, previousSnapshot, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Withdrawing, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("decision-plan-withdraw", result.Transition.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_WithPauseActivityDirective_TransitionsToHiding()
    {
        var sessionId = new SessionId("test-4");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var metadata = new Dictionary<string, string>();
        var directive = new DecisionDirective(
            "pause-1",
            DecisionDirectiveKind.PauseActivity,
            90,
            "ThreatResponsePolicy",
            null, null, null,
            metadata,
            []);
        var plan = new DecisionPlan(
            sessionId, _testNow, DecisionPlanStatus.Ready,
            [directive], [],
            new PolicyExecutionSummary([], [], [], [], 1, 1, new Dictionary<string, int>()),
            []);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, previousSnapshot, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Hiding, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("decision-plan-pause-activity", result.Transition.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_NavigationInProgress_TransitionsToTraveling()
    {
        var sessionId = new SessionId("test-5");
        var navigation = new NavigationState(
            NavigationStatus.InProgress, false,
            "Market", "Direct", 45.0,
            _testNow.AddMinutes(-5), _testNow);
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow) with
        {
            Navigation = navigation
        };
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, previousSnapshot, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Traveling, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("navigation-in-progress", result.Transition.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_ActiveCombat_TransitionsToEngaging()
    {
        var sessionId = new SessionId("test-6");
        var combat = new CombatState(
            CombatStatus.Engaged, "Offensive",
            true, false,
            _testNow.AddSeconds(-30), _testNow);
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow) with
        {
            Combat = combat
        };
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        var context = new SessionActivityEvaluationContext(
            sessionId, domain, plan, risk, previousSnapshot, null, _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Engaging, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("combat-engaged", result.Transition.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_HistoryPreservesAllTransitions()
    {
        var sessionId = new SessionId("test-7");
        var previousSnapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow);

        // First transition to Traveling
        var navigation = new NavigationState(
            NavigationStatus.InProgress, false,
            "Site", "Route", 25.0,
            _testNow, _testNow);
        var domain1 = SessionDomainState.CreateBootstrap(sessionId, _testNow) with
        {
            Navigation = navigation
        };
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);

        var context1 = new SessionActivityEvaluationContext(
            sessionId, domain1, plan, risk, previousSnapshot, null, _testNow);
        var result1 = await _evaluator.EvaluateAsync(context1, CancellationToken.None);

        Assert.Single(result1.NewSnapshot.History);
        Assert.Equal(SessionActivityStateKind.Traveling, result1.NewSnapshot.CurrentState);

        // Second transition to Arriving
        var navigation2 = new NavigationState(
            NavigationStatus.Idle, true,
            "Site", null, 100.0,
            _testNow, _testNow.AddSeconds(10));
        var domain2 = domain1 with { Navigation = navigation2 };

        var context2 = new SessionActivityEvaluationContext(
            sessionId, domain2, plan, risk, result1.NewSnapshot, null, _testNow.AddSeconds(10));
        var result2 = await _evaluator.EvaluateAsync(context2, CancellationToken.None);

        Assert.Equal(2, result2.NewSnapshot.History.Count);
        Assert.Equal(SessionActivityStateKind.Arriving, result2.NewSnapshot.CurrentState);
        Assert.Equal(SessionActivityStateKind.Traveling, result2.NewSnapshot.PreviousState);
    }

    [Fact]
    public async Task EvaluateAsync_RecoveryBackoff_TransitionsToRecovering()
    {
        var sessionId = new SessionId("test-recovery-backoff");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var recovery = SessionRecoverySnapshot.Create(sessionId) with
        {
            RecoveryStatus = SessionRecoveryStatus.Backoff,
            CircuitBreakerState = SessionRecoveryCircuitState.Closed,
            IsBlockedFromRecoveryAttempts = true
        };

        var context = new SessionActivityEvaluationContext(
            sessionId,
            domain,
            plan,
            risk,
            SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow),
            recovery,
            _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Recovering, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("recovery-backoff-active", result.Transition.ReasonCode);
    }

    [Fact]
    public async Task EvaluateAsync_RecoveryExhausted_TransitionsToFaulted()
    {
        var sessionId = new SessionId("test-recovery-exhausted");
        var domain = SessionDomainState.CreateBootstrap(sessionId, _testNow);
        var plan = DecisionPlan.Empty(sessionId, _testNow);
        var risk = RiskAssessmentResult.Empty(sessionId, _testNow);
        var recovery = SessionRecoverySnapshot.Create(sessionId) with
        {
            RecoveryStatus = SessionRecoveryStatus.Exhausted,
            CircuitBreakerState = SessionRecoveryCircuitState.Open,
            AdapterHealthState = SessionAdapterHealthState.Exhausted,
            IsBlockedFromRecoveryAttempts = true
        };

        var context = new SessionActivityEvaluationContext(
            sessionId,
            domain,
            plan,
            risk,
            SessionActivitySnapshot.CreateBootstrap(sessionId, _testNow),
            recovery,
            _testNow);

        var result = await _evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(SessionActivityStateKind.Faulted, result.NewSnapshot.CurrentState);
        Assert.NotNull(result.Transition);
        Assert.Equal("recovery-adapter-exhausted", result.Transition.ReasonCode);
    }
}
