using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Infrastructure.Queues;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Policy;

public sealed class PolicyEngineTests
{
    [Fact]
    public async Task AbortPolicy_ReturnsAbortDirectiveForFaultedRuntime()
    {
        var sessionId = new SessionId("policy-abort");
        var context = CreateContext(sessionId, runtimeStatus: SessionStatus.Faulted);
        var result = await new AbortPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidAbort);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Abort);
    }

    [Fact]
    public async Task ThreatResponsePolicy_ReturnsWithdrawForCriticalWithdrawRisk()
    {
        var sessionId = new SessionId("policy-threat");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.Critical, TopSuggestedPolicy = "Withdraw", TopEntityLabel = "critical-alert" }
            },
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Withdraw, RiskSeverity.Critical, priority: 900));

        var result = await new ThreatResponsePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidBlock);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Withdraw);
    }

    [Fact]
    public async Task TargetPrioritizationPolicy_SelectsTopPrioritizedThreat()
    {
        var sessionId = new SessionId("policy-target");
        var context = CreateContext(
            sessionId,
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Prioritize, RiskSeverity.High, priority: 700));

        var result = await new TargetPrioritizationPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.PrioritizeTarget, directive.DirectiveKind);
        Assert.Equal("candidate-1", directive.TargetId);
    }

    [Fact]
    public async Task ResourceUsagePolicy_ConservesOrWithdrawsForDegradedResources()
    {
        var sessionId = new SessionId("policy-resource");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Resources = ResourceState.CreateDefault() with { IsDegraded = true, EnergyPercent = 25 }
            });

        var result = await new ResourceUsagePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.ConserveResource);
    }

    [Fact]
    public async Task TransitPolicy_ReturnsWaitWhileTransitioning()
    {
        var sessionId = new SessionId("policy-transit");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.InProgress, IsTransitioning = true, DestinationLabel = "next-site" }
            });

        var result = await new TransitPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidBlock);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Wait);
    }

    [Fact]
    public async Task SelectNextSitePolicy_EmitsSiteSelectionWhenIdleAndSafe()
    {
        var sessionId = new SessionId("policy-site");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.None, IsSafe = true }
            });

        var result = await new SelectNextSitePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
    }

    [Fact]
    public void Aggregator_AbortOverridesAllDirectives()
    {
        var sessionId = new SessionId("aggregate-abort");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("AbortPolicy", Directive("AbortPolicy", DecisionDirectiveKind.Abort, 1000), didBlock: true, didAbort: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Aborting, plan.PlanStatus);
        Assert.Single(plan.Directives);
        Assert.Equal(DecisionDirectiveKind.Abort, plan.Directives[0].DirectiveKind);
        Assert.Equal(1, plan.Summary.SuppressedDirectiveCounts["AbortPolicy"]);
    }

    [Fact]
    public void Aggregator_ThreatResponseOverridesSiteSelection()
    {
        var sessionId = new SessionId("aggregate-threat");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("ThreatResponsePolicy", Directive("ThreatResponsePolicy", DecisionDirectiveKind.Withdraw, 900), didBlock: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Blocked, plan.PlanStatus);
        Assert.DoesNotContain(plan.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
    }

    [Fact]
    public void Aggregator_TransitWaitSuppressesLowerPriorityActivity()
    {
        var sessionId = new SessionId("aggregate-transit");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("TransitPolicy", Directive("TransitPolicy", DecisionDirectiveKind.Wait, 650), didBlock: true),
                Result("TargetPrioritizationPolicy", Directive("TargetPrioritizationPolicy", DecisionDirectiveKind.PrioritizeTarget, 600)),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Blocked, plan.PlanStatus);
        Assert.Single(plan.Directives);
        Assert.Equal(DecisionDirectiveKind.Wait, plan.Directives[0].DirectiveKind);
    }

    [Fact]
    public async Task PolicyEngine_EvaluatesPersistsAndKeepsSessionsIsolated()
    {
        var alphaId = new SessionId("engine-alpha");
        var betaId = new SessionId("engine-beta");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var registry = new InMemorySessionRegistry();
        var stateStore = new InMemorySessionStateStore();
        var uiStore = new InMemorySessionUiStateStore();
        var domainStore = new InMemorySessionDomainStateStore();
        var semanticStore = new InMemorySessionSemanticExtractionStore();
        var riskStore = new InMemorySessionRiskAssessmentStore();
        var queue = new ChannelBasedWorkQueue();
        var planStore = new InMemorySessionDecisionPlanStore();
        var options = new SessionHostOptions();

        foreach (var sessionId in new[] { alphaId, betaId })
        {
            var definition = CreateDefinition(sessionId);
            await registry.RegisterAsync(definition, CancellationToken.None);
            await stateStore.InitializeAsync(SessionRuntimeState.Create(definition, clock.UtcNow) with { CurrentStatus = SessionStatus.Running }, CancellationToken.None);
            await uiStore.InitializeAsync(SessionUiState.Create(sessionId), CancellationToken.None);
            await domainStore.InitializeAsync(
                CreateDomain(sessionId) with { Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.None, IsSafe = true } },
                CancellationToken.None);
        }

        var engine = new DefaultPolicyEngine(
            options,
            registry,
            stateStore,
            queue,
            uiStore,
            domainStore,
            semanticStore,
            riskStore,
            CreatePolicies(options),
            new DefaultDecisionPlanAggregator(options),
            planStore,
            clock,
            NullLogger<DefaultPolicyEngine>.Instance);

        var alphaPlan = await engine.EvaluateAsync(alphaId, CancellationToken.None);

        Assert.Equal(alphaId, alphaPlan.SessionId);
        Assert.Contains(alphaPlan.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
        Assert.NotNull(await planStore.GetLatestAsync(alphaId, CancellationToken.None));
        Assert.Null(await planStore.GetLatestAsync(betaId, CancellationToken.None));
    }

    private static IReadOnlyList<IPolicy> CreatePolicies(SessionHostOptions options) =>
    [
        new AbortPolicy(options),
        new ThreatResponsePolicy(options),
        new TransitPolicy(options),
        new ResourceUsagePolicy(options),
        new TargetPrioritizationPolicy(options),
        new SelectNextSitePolicy(options)
    ];

    private static PolicyEvaluationContext CreateContext(
        SessionId sessionId,
        SessionStatus runtimeStatus = SessionStatus.Running,
        SessionDomainState? domain = null,
        RiskAssessmentResult? riskAssessment = null) =>
        new(
            sessionId,
            new SessionSnapshot(
                CreateDefinition(sessionId),
                SessionRuntimeState.Create(CreateDefinition(sessionId), DateTimeOffset.Parse("2026-04-15T12:00:00Z")) with { CurrentStatus = runtimeStatus },
                PendingWorkItems: 0),
            SessionUiState.Create(sessionId),
            domain ?? CreateDomain(sessionId),
            UiSemanticExtractionResult.Empty(sessionId, DateTimeOffset.Parse("2026-04-15T12:00:00Z")),
            riskAssessment,
            ResolvedDesktopTargetContext: null,
            DesktopSessionAttachment: null,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));

    private static SessionDefinition CreateDefinition(SessionId sessionId) =>
        new(
            sessionId,
            $"{sessionId.Value}-display",
            Enabled: true,
            TickInterval: TimeSpan.FromMilliseconds(100),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 3,
            InitialBackoff: TimeSpan.FromMilliseconds(100),
            Tags: []);

    private static SessionDomainState CreateDomain(SessionId sessionId) =>
        SessionDomainState.CreateBootstrap(sessionId, DateTimeOffset.Parse("2026-04-15T12:00:00Z")) with
        {
            Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.Idle },
            Combat = CombatState.CreateDefault() with { Status = CombatStatus.Idle },
            Target = TargetState.CreateDefault(),
            Location = LocationState.CreateDefault() with { ContextLabel = "unit-worksite", IsUnknown = false }
        };

    private static RiskAssessmentResult CreateRisk(
        SessionId sessionId,
        RiskPolicySuggestion policySuggestion,
        RiskSeverity severity,
        int priority) =>
        new(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                new RiskEntityAssessment(
                    "candidate-1",
                    RiskEntitySource.Target,
                    "candidate-label",
                    "candidate-type",
                    ["active"],
                    RiskDisposition.Threat,
                    severity,
                    priority,
                    policySuggestion,
                    "test-rule",
                    ["test reason"],
                    1,
                    new Dictionary<string, string>())
            ],
            new RiskAssessmentSummary(
                SafeCount: 0,
                UnknownCount: 0,
                ThreatCount: 1,
                HighestSeverity: severity,
                HighestPriority: priority,
                HasWithdrawPolicy: policySuggestion == RiskPolicySuggestion.Withdraw,
                TopCandidateId: "candidate-1",
                TopCandidateName: "candidate-label",
                TopCandidateType: "candidate-type",
                TopSuggestedPolicy: policySuggestion),
            []);

    private static PolicyEvaluationResult Result(
        string policyName,
        DecisionDirective directive,
        bool didBlock = false,
        bool didAbort = false) =>
        new(policyName, [directive], [], [], DidMatch: true, didBlock, didAbort);

    private static DecisionDirective Directive(string policyName, DecisionDirectiveKind kind, int priority) =>
        new(
            $"{policyName}:{kind}".ToLowerInvariant(),
            kind,
            priority,
            policyName,
            TargetId: null,
            TargetLabel: null,
            SuggestedPolicy: kind.ToString(),
            new Dictionary<string, string>(),
            []);
}
