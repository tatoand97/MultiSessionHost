using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Policy;

public sealed class DecisionPlanExecutionTests
{
    [Fact]
    public async Task ExecuteAsync_ObserveDirective_ReturnsSucceeded()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);
        var context = CreateContext(CreatePlan("exec-observe", Directive("observe-1", DecisionDirectiveKind.Observe)));

        var result = await fixture.Executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Succeeded, result.ExecutionStatus);
        var directiveResult = Assert.Single(result.DirectiveResults);
        Assert.Equal(DecisionDirectiveExecutionStatus.Succeeded, directiveResult.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WaitDirective_ReturnsDeferredUntilUtc()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new WaitDirectiveHandler()]);
        var plan = CreatePlan(
            "exec-wait",
            Directive(
                "wait-1",
                DecisionDirectiveKind.Wait,
                metadata: new Dictionary<string, string>
                {
                    ["notBeforeUtc"] = "2026-04-15T12:00:10Z"
                }));

        var result = await fixture.Executor.ExecuteAsync(CreateContext(plan), CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Deferred, result.ExecutionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-04-15T12:00:10Z"), result.DeferredUntilUtc);
        var directiveResult = Assert.Single(result.DirectiveResults);
        Assert.Equal(DecisionDirectiveExecutionStatus.Deferred, directiveResult.Status);
    }

    [Fact]
    public async Task ExecuteAsync_NonWaitDirectiveWithFutureNotBefore_IsDeferredBeforeHandling()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);
        var plan = CreatePlan(
            "exec-not-before",
            Directive(
                "observe-1",
                DecisionDirectiveKind.Observe,
                metadata: new Dictionary<string, string>
                {
                    ["notBeforeUtc"] = "2026-04-15T12:00:10Z"
                }));

        var result = await fixture.Executor.ExecuteAsync(CreateContext(plan), CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Deferred, result.ExecutionStatus);
        Assert.Equal(DateTimeOffset.Parse("2026-04-15T12:00:10Z"), result.DeferredUntilUtc);
        var directiveResult = Assert.Single(result.DirectiveResults);
        Assert.Equal(DecisionDirectiveExecutionStatus.Deferred, directiveResult.Status);
        Assert.Contains("Observe", result.Summary.ExecutedDirectiveKinds);
    }

    [Fact]
    public async Task ExecuteAsync_IdenticalPlanWithinSuppressionWindow_IsSuppressed()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);
        var context = CreateContext(CreatePlan("exec-suppress", Directive("observe-1", DecisionDirectiveKind.Observe)));

        var first = await fixture.Executor.ExecuteAsync(context, CancellationToken.None);
        var second = await fixture.Executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Skipped, second.ExecutionStatus);
        Assert.Contains(second.Warnings, warning => warning.Contains("suppressed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(first.PlanFingerprint, second.PlanFingerprint);
    }

    [Fact]
    public async Task ExecuteAsync_ChangedFingerprint_ExecutesAgain()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);

        var first = await fixture.Executor.ExecuteAsync(
            CreateContext(CreatePlan("exec-replay", Directive("observe-1", DecisionDirectiveKind.Observe))),
            CancellationToken.None);
        var second = await fixture.Executor.ExecuteAsync(
            CreateContext(CreatePlan("exec-replay", Directive("observe-2", DecisionDirectiveKind.Observe))),
            CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Succeeded, second.ExecutionStatus);
        Assert.NotEqual(first.PlanFingerprint, second.PlanFingerprint);
    }

    [Fact]
    public async Task ExecuteAsync_UnhandledDirective_IsRecordedAndDoesNotThrow()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);
        var context = CreateContext(CreatePlan("exec-unhandled", Directive("select-site-1", DecisionDirectiveKind.SelectSite)));

        var result = await fixture.Executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.NoOp, result.ExecutionStatus);
        var directive = Assert.Single(result.DirectiveResults);
        Assert.Equal(DecisionDirectiveExecutionStatus.NotHandled, directive.Status);
    }

    [Fact]
    public async Task ExecuteAsync_AbortDirective_StopsLaterDirectives()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var gateway = new RecordingSessionControlGateway();
        var fixture = CreateFixture(clock, [new AbortDirectiveHandler(gateway, clock), new ObserveDirectiveHandler()]);
        var plan = CreatePlan(
            "exec-abort",
            Directive("abort-1", DecisionDirectiveKind.Abort),
            Directive("observe-1", DecisionDirectiveKind.Observe, priority: 100));

        var result = await fixture.Executor.ExecuteAsync(CreateContext(plan), CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Aborted, result.ExecutionStatus);
        Assert.Single(gateway.AbortCalls);
        Assert.Equal(DecisionDirectiveExecutionStatus.Aborted, result.DirectiveResults[0].Status);
        Assert.Equal(DecisionDirectiveExecutionStatus.Skipped, result.DirectiveResults[1].Status);
        Assert.Contains("Abort", result.Summary.ExecutedDirectiveKinds);
    }

    [Fact]
    public async Task ExecuteLatestAsync_PausedPolicy_ReturnsSkippedExecution()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var fixture = CreateFixture(clock, [new ObserveDirectiveHandler()]);
        var sessionId = new SessionId("exec-paused");
        var plan = CreatePlan(sessionId.Value, Directive("observe-1", DecisionDirectiveKind.Observe));
        await fixture.PlanStore.UpdateAsync(sessionId, plan, CancellationToken.None);
        await fixture.PolicyControlService.PauseAsync(
            sessionId,
            new PolicyControlActionRequest("policy:test-paused", "paused for test", "tester", new Dictionary<string, string>()),
            CancellationToken.None);

        var result = await fixture.Executor.ExecuteLatestAsync(sessionId, wasAutoExecuted: false, CancellationToken.None);

        Assert.Equal(DecisionPlanExecutionStatus.Skipped, result.ExecutionStatus);
        Assert.Contains(result.Warnings, warning => warning.Contains("paused", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.DirectiveResults);
    }

    [Fact]
    public async Task PauseActivityHandler_UsesSessionControlGateway()
    {
        var sessionId = new SessionId("exec-pause");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var gateway = new RecordingSessionControlGateway(() => clock.Advance(TimeSpan.FromMilliseconds(25)));
        var handler = new PauseActivityDirectiveHandler(gateway, clock);
        var context = CreateContext(CreatePlan(sessionId.Value, Directive("pause-1", DecisionDirectiveKind.PauseActivity)));

        var result = await handler.ExecuteAsync(
            new DecisionDirectiveExecutionContext(
                sessionId,
                context.DecisionPlan,
                DomainState: null,
                RiskAssessment: null,
                ActivitySnapshot: null,
                ExecutionStartedAtUtc: DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
                WasAutoExecuted: false),
            context.DecisionPlan.Directives[0],
            CancellationToken.None);

        Assert.Equal(DecisionDirectiveExecutionStatus.Succeeded, result.Status);
        Assert.Single(gateway.PauseCalls);
        Assert.Equal(sessionId, gateway.PauseCalls[0]);
        Assert.Equal(DateTimeOffset.Parse("2026-04-15T12:00:00Z"), result.StartedAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-04-15T12:00:00.025Z"), result.CompletedAtUtc);
    }

    private static (DefaultDecisionPlanExecutor Executor, InMemorySessionDecisionPlanExecutionStore Store, InMemorySessionDecisionPlanStore PlanStore, DefaultSessionPolicyControlService PolicyControlService) CreateFixture(
        FakeClock clock,
        IReadOnlyList<IDecisionDirectiveHandler> handlers)
    {
        var options = new SessionHostOptions
        {
            DecisionExecution = new DecisionExecutionOptions
            {
                EnableDecisionExecution = true,
                AutoExecuteAfterEvaluation = false,
                MaxHistoryEntries = 10,
                RepeatSuppressionWindowMs = 1_000,
                FailOnUnhandledBlockingDirective = false,
                RecordNoOpExecutions = true
            },
            Sessions = [TestOptionsFactory.Session("exec-session", startupDelayMs: 0)]
        };

        var planStore = new InMemorySessionDecisionPlanStore(options);
        var executionStore = new InMemorySessionDecisionPlanExecutionStore(options);
        var policyControlStore = new InMemorySessionPolicyControlStore(options, clock);
        var policyControlService = new DefaultSessionPolicyControlService(policyControlStore);
        var executor = new DefaultDecisionPlanExecutor(
            options,
            planStore,
            new InMemorySessionDomainStateStore(),
            new InMemorySessionRiskAssessmentStore(),
            new InMemorySessionActivityStateStore(),
            executionStore,
            policyControlService,
            new MultiSessionHost.Desktop.Persistence.NoOpRuntimePersistenceCoordinator(),
            handlers,
            clock,
            NullLogger<DefaultDecisionPlanExecutor>.Instance);

        return (executor, executionStore, planStore, policyControlService);
    }

    private static DecisionPlanExecutionContext CreateContext(DecisionPlan plan)
    {
        var sessionId = plan.SessionId;
        var now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");
        return new DecisionPlanExecutionContext(
            sessionId,
            plan,
            SessionDomainState.CreateBootstrap(sessionId, now),
            RiskAssessmentResult.Empty(sessionId, now),
            SessionActivitySnapshot.CreateBootstrap(sessionId, now),
            now,
            WasAutoExecuted: false);
    }

    private static DecisionPlan CreatePlan(string sessionId, params DecisionDirective[] directives)
    {
        var parsedSessionId = new SessionId(sessionId);
        return new DecisionPlan(
            parsedSessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            DecisionPlanStatus.Ready,
            directives,
            [],
            new PolicyExecutionSummary([], [], [], [], directives.Length, directives.Length, new Dictionary<string, int>()),
            []);
    }

    private static DecisionDirective Directive(
        string id,
        DecisionDirectiveKind kind,
        int priority = 500,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            id,
            kind,
            priority,
            "TestPolicy",
            TargetId: null,
            TargetLabel: null,
            SuggestedPolicy: null,
            metadata ?? new Dictionary<string, string>(),
            []);

    private sealed class RecordingSessionControlGateway : ISessionControlGateway
    {
        private readonly Action? _afterControlCall;

        public RecordingSessionControlGateway(Action? afterControlCall = null)
        {
            _afterControlCall = afterControlCall;
        }

        public List<SessionId> PauseCalls { get; } = [];

        public List<SessionId> AbortCalls { get; } = [];

        public ValueTask PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            PauseCalls.Add(sessionId);
            _afterControlCall?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask AbortSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            AbortCalls.Add(sessionId);
            _afterControlCall?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
