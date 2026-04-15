using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiDecisionExecutionIntegrationTests
{
    [Fact]
    public async Task EvaluateOnly_DoesNotCreateExecutionSnapshot()
    {
        const string sessionId = "api-exec-evaluate-only";
        var options = CreateNoOpOptions(sessionId, autoExecuteAfterEvaluation: false);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();

        var currentExecution = await client.GetAsync($"/sessions/{sessionId}/decision-execution");
        Assert.Equal(HttpStatusCode.NotFound, currentExecution.StatusCode);
    }

    [Fact]
    public async Task AutoExecuteEnabled_UiRefreshExecutesLatestPlanAfterEvaluation()
    {
        const int basePort = 7980;
        const string sessionId = "api-exec-auto-refresh";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = CreateDesktopOptions(sessionId, basePort, autoExecuteAfterEvaluation: true);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitForRunningAsync(harness, new SessionId(sessionId));
        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var currentExecution = await client.GetAsync($"/sessions/{sessionId}/decision-execution");
        var payload = await currentExecution.Content.ReadFromJsonAsync<DecisionPlanExecutionDto>();

        Assert.Equal(HttpStatusCode.OK, currentExecution.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.SessionId);
        Assert.True(payload.WasAutoExecuted);
        Assert.NotEmpty(payload.DirectiveResults);

        _ = app;
    }

    [Fact]
    public async Task ManualExecuteEndpoint_ExecutesLatestStoredDecisionPlan()
    {
        const string sessionId = "api-exec-manual";
        var options = CreateNoOpOptions(sessionId, autoExecuteAfterEvaluation: false);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null);
        var payload = await response.Content.ReadFromJsonAsync<DecisionPlanExecutionDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(sessionId, payload!.SessionId);
        Assert.False(payload.WasAutoExecuted);
        Assert.NotEmpty(payload.DirectiveResults);
    }

    [Fact]
    public async Task ManualExecuteEndpoint_ReturnsConflictWhenNoDecisionPlanExists()
    {
        const string sessionId = "api-exec-noplan";
        var options = CreateNoOpOptions(sessionId, autoExecuteAfterEvaluation: false);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var response = await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DecisionExecutionHistory_IsBoundedByConfiguredLimit()
    {
        const string sessionId = "api-exec-history";
        var options = CreateNoOpOptions(sessionId, autoExecuteAfterEvaluation: false, historyLimit: 2, suppressionWindowMs: 0);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null)).EnsureSuccessStatusCode();

        var history = await client.GetFromJsonAsync<DecisionPlanExecutionHistoryDto>($"/sessions/{sessionId}/decision-execution/history");

        Assert.NotNull(history);
        Assert.Equal(2, history!.Entries.Count);
    }

    [Fact]
    public async Task DecisionExecutions_AreSessionIsolated()
    {
        const string alpha = "api-exec-alpha";
        const string beta = "api-exec-beta";
        var options = new SessionHostOptions
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            DecisionExecution = new DecisionExecutionOptions
            {
                EnableDecisionExecution = true,
                AutoExecuteAfterEvaluation = false,
                MaxHistoryEntries = 10,
                RepeatSuppressionWindowMs = 0
            },
            Sessions =
            [
                TestOptionsFactory.Session(alpha, enabled: false),
                TestOptionsFactory.Session(beta, enabled: false)
            ]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{alpha}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{alpha}/decision-plan/execute", content: null)).EnsureSuccessStatusCode();

        var alphaCurrent = await client.GetAsync($"/sessions/{alpha}/decision-execution");
        var betaCurrent = await client.GetAsync($"/sessions/{beta}/decision-execution");
        var all = await client.GetFromJsonAsync<DecisionPlanExecutionDto[]>("/decision-executions");

        Assert.Equal(HttpStatusCode.OK, alphaCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, betaCurrent.StatusCode);
        Assert.NotNull(all);
        Assert.Single(all!);
        Assert.Equal(alpha, all![0].SessionId);
    }

    private static SessionHostOptions CreateNoOpOptions(
        string sessionId,
        bool autoExecuteAfterEvaluation,
        int historyLimit = 20,
        int suppressionWindowMs = 1_000) =>
        new()
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            DecisionExecution = new DecisionExecutionOptions
            {
                EnableDecisionExecution = true,
                AutoExecuteAfterEvaluation = autoExecuteAfterEvaluation,
                MaxHistoryEntries = historyLimit,
                RepeatSuppressionWindowMs = suppressionWindowMs,
                FailOnUnhandledBlockingDirective = false,
                RecordNoOpExecutions = true
            },
            Sessions = [TestOptionsFactory.Session(sessionId, enabled: false)]
        };

    private static SessionHostOptions CreateDesktopOptions(
        string sessionId,
        int port,
        bool autoExecuteAfterEvaluation) =>
        new()
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DecisionExecution = new DecisionExecutionOptions
            {
                EnableDecisionExecution = true,
                AutoExecuteAfterEvaluation = autoExecuteAfterEvaluation,
                MaxHistoryEntries = 20,
                RepeatSuppressionWindowMs = 1_000,
                FailOnUnhandledBlockingDirective = false,
                RecordNoOpExecutions = true
            },
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding(sessionId, "test-app", port.ToString())],
            Sessions = [TestOptionsFactory.Session(sessionId, tickIntervalMs: 60_000, startupDelayMs: 0)]
        };

    private static async Task WaitForRunningAsync(WorkerHostHarness harness, params SessionId[] sessionIds)
    {
        await TestWait.UntilAsync(
            () => sessionIds.All(sessionId => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running),
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed sessions in time.");
    }
}
