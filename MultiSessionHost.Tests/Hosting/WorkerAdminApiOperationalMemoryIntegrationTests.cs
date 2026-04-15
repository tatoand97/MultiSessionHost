using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiOperationalMemoryIntegrationTests
{
    [Fact]
    public async Task MemoryEndpoints_ReturnSnapshotsAfterManualExecution()
    {
        const string sessionId = "api-memory-manual";
        var options = CreateNoOpOptions(sessionId);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null)).EnsureSuccessStatusCode();

        var snapshot = await client.GetFromJsonAsync<SessionOperationalMemorySnapshotDto>($"/sessions/{sessionId}/memory");
        var summary = await client.GetFromJsonAsync<SessionOperationalMemorySummaryDto>($"/sessions/{sessionId}/memory/summary");
        var history = await client.GetFromJsonAsync<SessionOperationalMemoryHistoryDto>($"/sessions/{sessionId}/memory/history");
        var all = await client.GetFromJsonAsync<SessionOperationalMemorySnapshotDto[]>("/memory");

        Assert.NotNull(snapshot);
        Assert.Equal(sessionId, snapshot!.SessionId);
        Assert.NotNull(summary);
        Assert.True(summary!.OutcomeObservationCount >= 1);
        Assert.NotNull(history);
        Assert.NotEmpty(history!.Entries);
        Assert.NotNull(all);
        Assert.Contains(all!, item => item.SessionId == sessionId);

        var memoryContextResponse = await client.GetAsync($"/sessions/{sessionId}/memory/context");
        memoryContextResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task MemoryEndpoint_ReturnsNotFoundForKnownSessionWithoutMemory()
    {
        const string sessionId = "api-memory-empty";
        var options = CreateNoOpOptions(sessionId);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var response = await client.GetAsync($"/sessions/{sessionId}/memory");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UiRefresh_UpdatesMemoryWhenAutoExecutionIsDisabled()
    {
        const int basePort = 8090;
        const string sessionId = "api-memory-refresh-off";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = CreateDesktopOptions(sessionId, basePort, autoExecuteAfterEvaluation: false);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitForRunningAsync(harness, new SessionId(sessionId));
        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var snapshot = await client.GetFromJsonAsync<SessionOperationalMemorySnapshotDto>($"/sessions/{sessionId}/memory");

        Assert.NotNull(snapshot);
        Assert.Equal(sessionId, snapshot!.SessionId);
        Assert.True(snapshot.Summary.KnownWorksiteCount > 0 || snapshot.Summary.ActiveRiskMemoryCount > 0 || snapshot.Summary.ActivePresenceMemoryCount > 0);

        _ = app;
    }

    [Fact]
    public async Task UiRefresh_UpdatesMemoryWhenAutoExecutionIsEnabled()
    {
        const int basePort = 8100;
        const string sessionId = "api-memory-refresh-on";

        await using var app = await TestDesktopAppProcessHost.StartAsync(sessionId, basePort);
        var options = CreateDesktopOptions(sessionId, basePort, autoExecuteAfterEvaluation: true);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitForRunningAsync(harness, new SessionId(sessionId));
        (await client.PostAsync($"/sessions/{sessionId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var snapshot = await client.GetFromJsonAsync<SessionOperationalMemorySnapshotDto>($"/sessions/{sessionId}/memory");

        Assert.NotNull(snapshot);
        Assert.Equal(sessionId, snapshot!.SessionId);
        Assert.True(snapshot.Summary.OutcomeObservationCount >= 1);

        _ = app;
    }

    private static SessionHostOptions CreateNoOpOptions(string sessionId) =>
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
                AutoExecuteAfterEvaluation = false,
                MaxHistoryEntries = 20,
                RepeatSuppressionWindowMs = 0,
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
                RepeatSuppressionWindowMs = 0,
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
