using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiDomainIntegrationTests
{
    [Fact]
    public async Task DomainEndpoint_ReturnsInitializedStateBeforeUiRefresh()
    {
        const string sessionId = "api-domain-initialized";
        var options = new SessionHostOptions
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            Sessions = [TestOptionsFactory.Session(sessionId, enabled: false)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var dto = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");

        Assert.NotNull(dto);
        Assert.Equal(sessionId, dto!.SessionId);
        Assert.Equal("Bootstrap", dto.Source);
        Assert.Equal(1, dto.Version);
        Assert.Equal("Unknown", dto.Navigation.Status);
        Assert.Equal("Idle", dto.Combat.Status);
        Assert.Equal("Unknown", dto.Threat.Severity);
        Assert.Equal("None", dto.Target.Status);
        Assert.True(dto.Location.IsUnknown);
    }

    [Fact]
    public async Task DomainEndpoint_ReturnsNotFoundForUnknownSession()
    {
        var options = new SessionHostOptions
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            Sessions = [TestOptionsFactory.Session("api-domain-known", enabled: false)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var response = await client.GetAsync("/sessions/api-domain-missing/domain");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UiRefresh_UpdatesDomainStateTimestampsVersionAndSource()
    {
        const int basePort = 7850;
        const string sessionId = "api-domain-refresh";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(sessionId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitUntilRunningAsync(harness, sessionId);

        var before = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");
        var refresh = await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null);
        refresh.EnsureSuccessStatusCode();
        var after = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal("Bootstrap", before!.Source);
        Assert.Equal("UiProjection", after!.Source);
        Assert.True(after.Version > before.Version);
        Assert.NotNull(after.CapturedAtUtc);
        Assert.True(after.UpdatedAtUtc >= before.UpdatedAtUtc);
        Assert.Equal("DesktopTestApp", after.Location.ContextLabel);
    }

    [Fact]
    public async Task DomainEndpoint_ReturnsAllSessionStatesAndKeepsSessionsIsolated()
    {
        const int basePort = 7860;
        const string alphaId = "api-domain-alpha";
        const string betaId = "api-domain-beta";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(alphaId, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await TestWait.UntilAsync(
            () =>
            {
                var alpha = harness.Coordinator.GetSession(new SessionId(alphaId));
                var beta = harness.Coordinator.GetSession(new SessionId(betaId));
                return alpha?.Runtime.CurrentStatus == SessionStatus.Running && beta?.Runtime.CurrentStatus == SessionStatus.Running;
            },
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start both desktop-backed sessions in time.");

        await (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).Content.ReadFromJsonAsync<SessionUiRefreshDto>();

        var all = await client.GetFromJsonAsync<SessionDomainStateDto[]>("/domain");
        var alpha = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{alphaId}/domain");
        var beta = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{betaId}/domain");

        Assert.NotNull(all);
        Assert.NotNull(alpha);
        Assert.NotNull(beta);
        Assert.Equal(2, all!.Length);
        Assert.Equal("UiProjection", alpha!.Source);
        Assert.Equal("Bootstrap", beta!.Source);
        Assert.NotEqual(alpha.Version, beta.Version);
    }

    [Fact]
    public async Task RuntimeBindingChanges_DoNotBreakDomainSnapshotRetrieval()
    {
        const string sessionId = "api-domain-binding";
        var options = new SessionHostOptions
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding(sessionId, "test-app", "7870")],
            Sessions = [TestOptionsFactory.Session(sessionId, enabled: false)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var updateResponse = await client.PutAsJsonAsync(
            $"/bindings/{sessionId}",
            new SessionTargetBindingUpsertRequest(
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7871" },
                null));
        updateResponse.EnsureSuccessStatusCode();

        var domainResponse = await client.GetAsync($"/sessions/{sessionId}/domain");
        var dto = await domainResponse.Content.ReadFromJsonAsync<SessionDomainStateDto>();

        Assert.Equal(HttpStatusCode.OK, domainResponse.StatusCode);
        Assert.NotNull(dto);
        Assert.Equal(sessionId, dto!.SessionId);
        Assert.Equal("Bootstrap", dto.Source);
    }

    [Fact]
    public async Task UiRefresh_PopulatesSemanticExtractionAndFeedsDomainState()
    {
        const int basePort = 7880;
        const string sessionId = "api-semantic-refresh";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(sessionId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitUntilRunningAsync(harness, sessionId);

        var refresh = await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var semantic = await client.GetFromJsonAsync<UiSemanticExtractionResultDto>($"/sessions/{sessionId}/semantic");
        var summary = await client.GetFromJsonAsync<SemanticSummaryDto>($"/sessions/{sessionId}/semantic/summary");
        var domain = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");

        Assert.NotNull(semantic);
        Assert.NotNull(summary);
        Assert.NotNull(domain);
        Assert.Contains(semantic!.Lists, static item => item.NodeId == "itemsListBox" && item.ItemCount == 3);
        Assert.Contains(semantic.TransitStates, static item => item.NodeIds.Contains("progressBar"));
        Assert.Contains(semantic.Resources, static item => item.NodeId == "resourceProgressBar");
        Assert.Contains(semantic.Capabilities, static item => item.NodeId == "capabilityCheckBox");
        Assert.Contains(semantic.PresenceEntities, static item => item.NodeId == "presenceListBox" && item.Count == 2);
        Assert.True(summary!.ListCount > 0);
        Assert.Equal("UiProjection", domain!.Source);
        Assert.NotNull(domain.Resources.HealthPercent ?? domain.Resources.CapacityPercent ?? domain.Resources.EnergyPercent);
        Assert.Equal("DesktopTestApp", domain.Location.ContextLabel);
        Assert.Contains("presence", domain.Location.SubLocationLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SemanticExtraction_RemainsSessionIsolated()
    {
        const int basePort = 7890;
        const string alphaId = "api-semantic-alpha";
        const string betaId = "api-semantic-beta";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(alphaId, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await TestWait.UntilAsync(
            () =>
            {
                var alpha = harness.Coordinator.GetSession(new SessionId(alphaId));
                var beta = harness.Coordinator.GetSession(new SessionId(betaId));
                return alpha?.Runtime.CurrentStatus == SessionStatus.Running && beta?.Runtime.CurrentStatus == SessionStatus.Running;
            },
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start both desktop-backed sessions in time.");

        (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var alphaSemantic = await client.GetFromJsonAsync<UiSemanticExtractionResultDto>($"/sessions/{alphaId}/semantic");
        var betaSemanticResponse = await client.GetAsync($"/sessions/{betaId}/semantic");
        var allSemantic = await client.GetFromJsonAsync<UiSemanticExtractionResultDto[]>("/semantic");

        Assert.NotNull(alphaSemantic);
        Assert.Equal(alphaId, alphaSemantic!.SessionId);
        Assert.Equal(HttpStatusCode.NotFound, betaSemanticResponse.StatusCode);
        Assert.NotNull(allSemantic);
        Assert.Single(allSemantic!);
        Assert.Equal(alphaId, allSemantic[0].SessionId);
    }

    [Fact]
    public async Task UiRefresh_PopulatesRiskAssessmentAndFeedsDomainThreatState()
    {
        const int basePort = 7910;
        const string sessionId = "api-risk-refresh";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = TestOptionsFactory.CreateDesktopTestAppOptionsWithRisk(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.GenericRiskClassification(),
            TestOptionsFactory.Session(sessionId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitUntilRunningAsync(harness, sessionId);

        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();
        var initialRisk = await client.GetFromJsonAsync<RiskAssessmentResultDto>($"/sessions/{sessionId}/risk");

        Assert.NotNull(initialRisk);
        Assert.Contains(initialRisk!.Entities, entity => entity.Disposition == "Safe" && entity.SuggestedPolicy == "Ignore");

        (await client.PostAsync($"/sessions/{sessionId}/nodes/tickButton/invoke", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var warningSummary = await client.GetFromJsonAsync<RiskAssessmentSummaryDto>($"/sessions/{sessionId}/risk/summary");
        var warningDomain = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");

        Assert.NotNull(warningSummary);
        Assert.NotNull(warningDomain);
        Assert.Equal("Critical", warningSummary!.HighestSeverity);
        Assert.True(warningSummary.ThreatCount > 0);
        Assert.Equal("Critical", warningDomain!.Threat.Severity);
        Assert.True(warningDomain.Threat.HostileCount > 0);
        Assert.Contains(warningDomain.Threat.Signals, signal => signal.Contains("warning-alert", StringComparison.Ordinal));

        var selectResponse = await client.PostAsJsonAsync(
            $"/sessions/{sessionId}/nodes/itemsListBox/select",
            new NodeSelectCommandRequest($"{sessionId}-item-2", Metadata: null));
        selectResponse.EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var prioritized = await client.GetFromJsonAsync<RiskAssessmentSummaryDto>($"/sessions/{sessionId}/risk/summary");
        var entities = await client.GetFromJsonAsync<RiskEntityAssessmentDto[]>($"/sessions/{sessionId}/risk/entities");

        Assert.NotNull(prioritized);
        Assert.NotNull(entities);
        Assert.Equal("Prioritize", prioritized!.TopSuggestedPolicy);
        Assert.Contains(entities!, entity => entity.MatchedRuleName == "priority-selection" && entity.SuggestedPolicy == "Prioritize");
    }

    [Fact]
    public async Task RiskAssessment_RemainsSessionIsolatedAndSurvivesBindingChanges()
    {
        const int basePort = 7920;
        const string alphaId = "api-risk-alpha";
        const string betaId = "api-risk-beta";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);
        var options = TestOptionsFactory.CreateDesktopTestAppOptionsWithRisk(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.GenericRiskClassification(),
            TestOptionsFactory.Session(alphaId, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await TestWait.UntilAsync(
            () =>
            {
                var alpha = harness.Coordinator.GetSession(new SessionId(alphaId));
                var beta = harness.Coordinator.GetSession(new SessionId(betaId));
                return alpha?.Runtime.CurrentStatus == SessionStatus.Running && beta?.Runtime.CurrentStatus == SessionStatus.Running;
            },
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start both desktop-backed sessions in time.");

        (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(
            $"/bindings/{alphaId}",
            new SessionTargetBindingUpsertRequest(
                "test-app",
                new Dictionary<string, string> { ["Port"] = basePort.ToString() },
                null))).EnsureSuccessStatusCode();

        var alphaRisk = await client.GetFromJsonAsync<RiskAssessmentResultDto>($"/sessions/{alphaId}/risk");
        var betaRiskResponse = await client.GetAsync($"/sessions/{betaId}/risk");
        var allRisk = await client.GetFromJsonAsync<RiskAssessmentResultDto[]>("/risk");

        Assert.NotNull(alphaRisk);
        Assert.Equal(alphaId, alphaRisk!.SessionId);
        Assert.Equal(HttpStatusCode.NotFound, betaRiskResponse.StatusCode);
        Assert.NotNull(allRisk);
        Assert.Single(allRisk!);
        Assert.Equal(alphaId, allRisk[0].SessionId);
    }

    [Fact]
    public async Task SemanticInspection_SurvivesBindingAndCoordinationEndpointsAndChangesAfterCommands()
    {
        const int basePort = 7900;
        const string sessionId = "api-semantic-command";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(sessionId, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitUntilRunningAsync(harness, sessionId);

        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();
        (await client.GetAsync($"/coordination/sessions/{sessionId}")).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(
            $"/bindings/{sessionId}",
            new SessionTargetBindingUpsertRequest(
                "test-app",
                new Dictionary<string, string> { ["Port"] = basePort.ToString() },
                null))).EnsureSuccessStatusCode();

        var selectResponse = await client.PostAsJsonAsync(
            $"/sessions/{sessionId}/nodes/itemsListBox/select",
            new NodeSelectCommandRequest($"{sessionId}-item-2", Metadata: null));
        selectResponse.EnsureSuccessStatusCode();

        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();
        var semantic = await client.GetFromJsonAsync<UiSemanticExtractionResultDto>($"/sessions/{sessionId}/semantic");
        var domain = await client.GetFromJsonAsync<SessionDomainStateDto>($"/sessions/{sessionId}/domain");

        Assert.NotNull(semantic);
        Assert.NotNull(domain);
        Assert.Contains(semantic!.Targets, static target => target.Selected || target.Active);
        Assert.True(domain!.Target.HasActiveTarget);
        Assert.Equal("Active", domain.Target.Status);
    }

    private static Task WaitUntilRunningAsync(WorkerHostHarness harness, string sessionId) =>
        TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(new SessionId(sessionId))?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed session in time.");
}
