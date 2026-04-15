using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiDecisionPlanIntegrationTests
{
    [Fact]
    public async Task UiRefresh_PersistsDecisionPlanForInspection()
    {
        const int basePort = 7980;
        const string sessionId = "api-decision-refresh";

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

        var refresh = await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var plan = await client.GetFromJsonAsync<DecisionPlanDto>($"/sessions/{sessionId}/decision-plan");
        var summary = await client.GetFromJsonAsync<DecisionPlanSummaryDto>($"/sessions/{sessionId}/decision-plan/summary");
        var directives = await client.GetFromJsonAsync<DecisionDirectiveDto[]>($"/sessions/{sessionId}/decision-plan/directives");

        Assert.NotNull(plan);
        Assert.NotNull(summary);
        Assert.NotNull(directives);
        Assert.Equal(sessionId, plan!.SessionId);
        Assert.Contains("AbortPolicy", plan.Summary.EvaluatedPolicies);
        Assert.Contains(plan.Directives, directive => directive.DirectiveKind is "SelectSite" or "Observe" or "Wait");
        Assert.Equal(plan.Directives.Count, summary!.DirectiveCount);
        Assert.Equal(plan.Directives.Count, directives!.Length);
    }

    [Fact]
    public async Task DecisionPlans_RemainSessionIsolatedAndSurviveBindingChanges()
    {
        const int basePort = 7990;
        const string alphaId = "api-decision-alpha";
        const string betaId = "api-decision-beta";

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

        var alphaPlan = await client.GetFromJsonAsync<DecisionPlanDto>($"/sessions/{alphaId}/decision-plan");
        var betaPlanResponse = await client.GetAsync($"/sessions/{betaId}/decision-plan");
        var allPlans = await client.GetFromJsonAsync<DecisionPlanDto[]>("/decision-plans");

        Assert.NotNull(alphaPlan);
        Assert.Equal(alphaId, alphaPlan!.SessionId);
        Assert.Equal(HttpStatusCode.NotFound, betaPlanResponse.StatusCode);
        Assert.NotNull(allPlans);
        Assert.Single(allPlans!);
        Assert.Equal(alphaId, allPlans[0].SessionId);
    }

    [Fact]
    public async Task DecisionPlanEvaluateEndpoint_ReturnsNotFoundForUnknownSession()
    {
        const string sessionId = "api-decision-known";
        var options = new MultiSessionHost.Core.Configuration.SessionHostOptions
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

        var response = await client.PostAsync("/sessions/api-decision-missing/decision-plan/evaluate", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task WaitUntilRunningAsync(WorkerHostHarness harness, string sessionId) =>
        TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(new SessionId(sessionId))?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed session in time.");
}
