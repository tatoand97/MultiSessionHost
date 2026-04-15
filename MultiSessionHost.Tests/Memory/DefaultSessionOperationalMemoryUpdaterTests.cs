using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Tests.Memory;

public sealed class DefaultSessionOperationalMemoryUpdaterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");

    [Fact]
    public async Task BootstrapWithNoSignals_ProducesEmptyValidSnapshot()
    {
        var sessionId = new SessionId("memory-bootstrap");
        var updater = CreateUpdater();

        var result = await updater.UpdateAsync(CreateContext(sessionId), CancellationToken.None);

        Assert.NotNull(result.Snapshot);
        Assert.Equal(sessionId, result.Snapshot!.SessionId);
        Assert.Equal(0, result.Snapshot.Summary.KnownWorksiteCount);
        Assert.Empty(result.AddedObservationRecords);
    }

    [Fact]
    public async Task SelectSiteAndArrival_UpdateWorksiteMemory()
    {
        var sessionId = new SessionId("memory-worksite");
        var updater = CreateUpdater();
        var plan = CreatePlan(
            sessionId,
            new DecisionDirective(
                "select-1",
                DecisionDirectiveKind.SelectSite,
                250,
                "test-policy",
                "site-a",
                "Site A",
                "SelectSite",
                new Dictionary<string, string> { ["siteLabel"] = "Site A" },
                []));

        var first = await updater.UpdateAsync(CreateContext(sessionId, decisionPlan: plan), CancellationToken.None);
        var arrivedDomain = CreateDomain(sessionId) with
        {
            Location = new LocationState("Site A", null, false, false, LocationConfidence.High, Now.AddMinutes(1), Now.AddMinutes(1)),
            Navigation = new NavigationState(NavigationStatus.Idle, false, null, null, null, Now.AddMinutes(-4), Now.AddMinutes(1))
        };

        var second = await updater.UpdateAsync(
            CreateContext(sessionId, previous: first.Snapshot, domainState: arrivedDomain, decisionPlan: plan, now: Now.AddMinutes(1)),
            CancellationToken.None);

        var worksite = Assert.Single(second.Snapshot!.KnownWorksites);
        Assert.Equal("worksite:site a", worksite.WorksiteKey);
        Assert.Equal(Now.AddMinutes(1), worksite.LastSelectedAtUtc);
        Assert.Equal(Now.AddMinutes(1), worksite.LastArrivedAtUtc);
        Assert.Equal(1, worksite.VisitCount);
    }

    [Fact]
    public async Task RepeatedWorksiteObservation_IncrementsFreshnessWithoutDuplicatingWorksite()
    {
        var sessionId = new SessionId("memory-repeat-worksite");
        var updater = CreateUpdater();
        var plan = CreatePlan(sessionId, CreateSelectSiteDirective());

        var first = await updater.UpdateAsync(CreateContext(sessionId, decisionPlan: plan), CancellationToken.None);
        var second = await updater.UpdateAsync(
            CreateContext(sessionId, previous: first.Snapshot, decisionPlan: plan, now: Now.AddMinutes(2)),
            CancellationToken.None);

        var worksite = Assert.Single(second.Snapshot!.KnownWorksites);
        Assert.Equal(Now, worksite.FirstObservedAtUtc);
        Assert.Equal(Now.AddMinutes(2), worksite.LastObservedAtUtc);
    }

    [Fact]
    public async Task RiskAssessment_UpdatesRiskMemoryDeterministically()
    {
        var sessionId = new SessionId("memory-risk");
        var updater = CreateUpdater();
        var risk = CreateRisk(sessionId, "entity-1", RiskSeverity.High, RiskPolicySuggestion.Prioritize);

        var first = await updater.UpdateAsync(CreateContext(sessionId, riskAssessment: risk), CancellationToken.None);
        var second = await updater.UpdateAsync(
            CreateContext(sessionId, previous: first.Snapshot, riskAssessment: risk, now: Now.AddMinutes(1)),
            CancellationToken.None);

        var observation = Assert.Single(second.Snapshot!.RecentRiskObservations);
        Assert.Equal("risk:entity-1", observation.ObservationId);
        Assert.Equal(2, observation.Count);
        Assert.Equal(RiskSeverity.High, second.Snapshot.Summary.TopRememberedRiskSeverity);
    }

    [Fact]
    public async Task PresenceSignals_UpdatePresenceMemoryAndWorksiteOccupancySignals()
    {
        var sessionId = new SessionId("memory-presence");
        var updater = CreateUpdater();
        var plan = CreatePlan(sessionId, CreateSelectSiteDirective());
        var semantic = UiSemanticExtractionResult.Empty(sessionId, Now) with
        {
            PresenceEntities =
            [
                new DetectedPresenceEntity("presence-node", "Operator", 1, [], PresenceEntityKind.Person, "visible", DetectionConfidence.High)
            ]
        };

        var result = await updater.UpdateAsync(CreateContext(sessionId, semanticExtraction: semantic, decisionPlan: plan), CancellationToken.None);

        Assert.Single(result.Snapshot!.RecentPresenceObservations);
        Assert.Contains("Operator", Assert.Single(result.Snapshot.KnownWorksites).OccupancySignals);
    }

    [Fact]
    public async Task TimingObservations_AccumulateMinMaxAverage()
    {
        var sessionId = new SessionId("memory-timing");
        var updater = CreateUpdater();
        var firstDomain = CreateDomain(sessionId) with
        {
            Navigation = new NavigationState(NavigationStatus.Idle, false, null, null, null, Now.AddSeconds(-10), Now),
            Location = new LocationState("Site A", null, false, false, LocationConfidence.High, Now, Now)
        };
        var secondDomain = firstDomain with
        {
            Navigation = firstDomain.Navigation with { StartedAtUtc = Now.AddMinutes(1).AddSeconds(-20), UpdatedAtUtc = Now.AddMinutes(1) },
            Location = firstDomain.Location with { ArrivedAtUtc = Now.AddMinutes(1), UpdatedAtUtc = Now.AddMinutes(1) }
        };

        var first = await updater.UpdateAsync(CreateContext(sessionId, domainState: firstDomain), CancellationToken.None);
        var second = await updater.UpdateAsync(
            CreateContext(sessionId, previous: first.Snapshot, domainState: secondDomain, now: Now.AddMinutes(1)),
            CancellationToken.None);

        var timing = Assert.Single(second.Snapshot!.RecentTimingObservations);
        Assert.Equal(2, timing.SampleCount);
        Assert.Equal(10_000, timing.MinDurationMs);
        Assert.Equal(20_000, timing.MaxDurationMs);
        Assert.Equal(15_000, timing.AverageDurationMs);
    }

    [Fact]
    public async Task ExecutionOutcome_CreatesOutcomeObservationAndUpdatesWorksiteCounts()
    {
        var sessionId = new SessionId("memory-outcome");
        var updater = CreateUpdater();
        var plan = CreatePlan(sessionId, CreateSelectSiteDirective());
        var execution = CreateExecution(sessionId, plan, DecisionPlanExecutionStatus.Succeeded);

        var result = await updater.UpdateAsync(CreateContext(sessionId, decisionPlan: plan, executionResult: execution), CancellationToken.None);

        Assert.Equal("success", Assert.Single(result.Snapshot!.RecentOutcomeObservations).ResultKind);
        Assert.Equal(1, Assert.Single(result.Snapshot.KnownWorksites).SuccessCount);
    }

    [Fact]
    public async Task NoExecutionStillUpdatesFromPlanRiskAndActivity()
    {
        var sessionId = new SessionId("memory-no-execution");
        var updater = CreateUpdater();
        var activity = SessionActivitySnapshot.CreateBootstrap(sessionId, Now) with
        {
            CurrentState = SessionActivityStateKind.SelectingWorksite
        };

        var result = await updater.UpdateAsync(
            CreateContext(
                sessionId,
                riskAssessment: CreateRisk(sessionId, "entity-1", RiskSeverity.Moderate, RiskPolicySuggestion.Observe),
                decisionPlan: CreatePlan(sessionId, CreateSelectSiteDirective()),
                activitySnapshot: activity),
            CancellationToken.None);

        Assert.Single(result.Snapshot!.KnownWorksites);
        Assert.Single(result.Snapshot.RecentRiskObservations);
        Assert.Empty(result.Snapshot.RecentOutcomeObservations);
    }

    [Fact]
    public async Task StaleObservations_AreMarkedWhenOlderThanThreshold()
    {
        var sessionId = new SessionId("memory-stale");
        var updater = CreateUpdater(staleAfterMinutes: 1);
        var first = await updater.UpdateAsync(
            CreateContext(sessionId, riskAssessment: CreateRisk(sessionId, "entity-1", RiskSeverity.Low, RiskPolicySuggestion.Observe)),
            CancellationToken.None);

        var second = await updater.UpdateAsync(
            CreateContext(sessionId, previous: first.Snapshot, now: Now.AddMinutes(2)),
            CancellationToken.None);

        Assert.True(Assert.Single(second.Snapshot!.RecentRiskObservations).IsStale);
        Assert.Equal(0, second.Snapshot.Summary.ActiveRiskMemoryCount);
    }

    [Fact]
    public async Task DisabledOperationalMemory_NoOpsSafely()
    {
        var sessionId = new SessionId("memory-disabled");
        var updater = CreateUpdater(enabled: false);

        var result = await updater.UpdateAsync(CreateContext(sessionId, decisionPlan: CreatePlan(sessionId, CreateSelectSiteDirective())), CancellationToken.None);

        Assert.Null(result.Snapshot);
        Assert.Empty(result.AddedObservationRecords);
        Assert.Contains("disabled", result.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultSessionOperationalMemoryUpdater CreateUpdater(bool enabled = true, int staleAfterMinutes = 60) =>
        new(new SessionHostOptions
        {
            OperationalMemory = new OperationalMemoryOptions
            {
                EnableOperationalMemory = enabled,
                StaleAfterMinutes = staleAfterMinutes
            }
        });

    private static SessionOperationalMemoryUpdateContext CreateContext(
        SessionId sessionId,
        SessionOperationalMemorySnapshot? previous = null,
        SessionDomainState? domainState = null,
        UiSemanticExtractionResult? semanticExtraction = null,
        RiskAssessmentResult? riskAssessment = null,
        DecisionPlan? decisionPlan = null,
        DecisionPlanExecutionResult? executionResult = null,
        SessionActivitySnapshot? activitySnapshot = null,
        DateTimeOffset? now = null) =>
        new(
            sessionId,
            previous,
            domainState,
            semanticExtraction,
            riskAssessment,
            decisionPlan,
            executionResult,
            activitySnapshot,
            now ?? Now);

    private static SessionDomainState CreateDomain(SessionId sessionId) =>
        SessionDomainState.CreateBootstrap(sessionId, Now);

    private static DecisionDirective CreateSelectSiteDirective() =>
        new(
            "select-site",
            DecisionDirectiveKind.SelectSite,
            250,
            "test-policy",
            "site-a",
            "Site A",
            "SelectSite",
            new Dictionary<string, string> { ["siteLabel"] = "Site A" },
            []);

    private static DecisionPlan CreatePlan(SessionId sessionId, params DecisionDirective[] directives) =>
        new(
            sessionId,
            Now,
            directives.Length == 0 ? DecisionPlanStatus.Idle : DecisionPlanStatus.Ready,
            directives,
            [],
            new PolicyExecutionSummary(["test-policy"], ["test-policy"], [], [], directives.Length, directives.Length, new Dictionary<string, int>()),
            []);

    private static RiskAssessmentResult CreateRisk(
        SessionId sessionId,
        string candidateId,
        RiskSeverity severity,
        RiskPolicySuggestion suggestion) =>
        new(
            sessionId,
            Now,
            [
                new RiskEntityAssessment(
                    candidateId,
                    RiskEntitySource.Presence,
                    "Entity One",
                    "Person",
                    ["observed"],
                    RiskDisposition.Threat,
                    severity,
                    100,
                    suggestion,
                    "test-rule",
                    ["matched"],
                    0.8,
                    new Dictionary<string, string>())
            ],
            new RiskAssessmentSummary(0, 0, 1, severity, 100, suggestion == RiskPolicySuggestion.Withdraw, candidateId, "Entity One", "Person", suggestion),
            []);

    private static DecisionPlanExecutionResult CreateExecution(
        SessionId sessionId,
        DecisionPlan plan,
        DecisionPlanExecutionStatus status) =>
        new(
            sessionId,
            "fingerprint-1",
            Now,
            Now,
            Now.AddMilliseconds(5),
            status,
            WasAutoExecuted: true,
            [
                new DecisionDirectiveExecutionResult(
                    plan.Directives[0].DirectiveId,
                    plan.Directives[0].DirectiveKind,
                    plan.Directives[0].SourcePolicy,
                    plan.Directives[0].Priority,
                    DecisionDirectiveExecutionStatus.Succeeded,
                    Now,
                    Now.AddMilliseconds(5),
                    "ok",
                    FailureCode: null,
                    DeferredUntilUtc: null,
                    plan.Directives[0].Metadata)
            ],
            new DecisionPlanExecutionSummary(1, 1, 0, 0, 0, 0, 0, 0, ["SelectSite"], [], []),
            DeferredUntilUtc: null,
            FailureReason: null,
            Warnings: [],
            Metadata: new Dictionary<string, string>());
}
