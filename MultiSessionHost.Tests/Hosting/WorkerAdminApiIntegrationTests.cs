using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiIntegrationTests
{
    [Fact]
    public async Task EnableAdminApiFalse_DoesNotExposeHttpServer()
    {
        var options = CreateOptions(enableAdminApi: false);

        await using var harness = await WorkerHostHarness.StartAsync(options);

        Assert.Null(harness.Client);
        Assert.Null(harness.BaseAddress);
        Assert.Null(harness.Host.Services.GetService<IServer>());
    }

    [Fact]
    public async Task EnableAdminApiTrue_ExposesEndpointsAgainstWorkerRuntime()
    {
        var options = CreateOptions(enableAdminApi: true);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var sessions = await client.GetFromJsonAsync<SessionInfoDto[]>("/sessions");
        var session = await client.GetFromJsonAsync<SessionInfoDto>("/sessions/alpha");

        Assert.NotNull(sessions);
        Assert.Single(sessions!);
        Assert.Equal("alpha", sessions[0].SessionId);
        Assert.NotNull(session);
        Assert.Equal("alpha", session!.SessionId);
        Assert.Single(harness.Registry.GetAll());

        var workerSession = harness.Coordinator.GetSession(new SessionId("alpha"));

        Assert.NotNull(workerSession);
        Assert.Equal(workerSession!.Runtime.CurrentStatus.ToString(), sessions[0].State.CurrentStatus);
        Assert.Equal(workerSession.Runtime.CurrentStatus.ToString(), session.State.CurrentStatus);
    }

    [Fact]
    public async Task StartStopPauseResume_ChangeTheRealWorkerState()
    {
        var options = CreateOptions(enableAdminApi: true);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);
        var sessionId = new SessionId("alpha");

        var startResponse = await client.PostAsJsonAsync("/sessions/alpha/start", new StartSessionRequest("integration test"));
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(5),
            "Session did not reach Running after /start.");
        await TestWait.UntilAsync(
            async () => (await harness.GetStateAsync("alpha").ConfigureAwait(false))?.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(5),
            "State store did not reflect Running after /start.");

        var pauseResponse = await client.PostAsJsonAsync("/sessions/alpha/pause", new PauseSessionRequest("integration test"));
        Assert.Equal(HttpStatusCode.Accepted, pauseResponse.StatusCode);
        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Paused,
            TimeSpan.FromSeconds(5),
            "Session did not reach Paused after /pause.");

        var resumeResponse = await client.PostAsJsonAsync("/sessions/alpha/resume", new ResumeSessionRequest("integration test"));
        Assert.Equal(HttpStatusCode.Accepted, resumeResponse.StatusCode);
        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(5),
            "Session did not return to Running after /resume.");

        var stopResponse = await client.PostAsJsonAsync("/sessions/alpha/stop", new StopSessionRequest("integration test"));
        Assert.Equal(HttpStatusCode.Accepted, stopResponse.StatusCode);
        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Stopped,
            TimeSpan.FromSeconds(5),
            "Session did not reach Stopped after /stop.");
        await TestWait.UntilAsync(
            async () => (await harness.GetStateAsync("alpha").ConfigureAwait(false))?.CurrentStatus == SessionStatus.Stopped,
            TimeSpan.FromSeconds(5),
            "State store did not reflect Stopped after /stop.");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsTheSameStateAsTheWorkerCoordinator()
    {
        var options = CreateOptions(enableAdminApi: true);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var health = await client.GetFromJsonAsync<ProcessHealthDto>("/health");
        var metrics = await client.GetFromJsonAsync<ProcessHealthDto>("/metrics");
        var internalHealth = harness.Coordinator.GetProcessHealth();

        Assert.NotNull(health);
        Assert.NotNull(metrics);
        Assert.Equal(internalHealth.ActiveSessions, health!.ActiveSessions);
        Assert.Equal(internalHealth.FaultedSessions, health.FaultedSessions);
        Assert.Equal(internalHealth.TotalTicksExecuted, health.TotalTicksExecuted);
        Assert.Equal(internalHealth.TotalErrors, health.TotalErrors);
        Assert.Equal(internalHealth.TotalRetries, health.TotalRetries);
        Assert.Equal(internalHealth.TotalHeartbeatsEmitted, health.TotalHeartbeatsEmitted);
        Assert.Equal(internalHealth.Sessions.Count, health.Sessions.Count);
        Assert.Equal(health.ActiveSessions, metrics!.ActiveSessions);
        Assert.Equal(health.FaultedSessions, metrics.FaultedSessions);
        Assert.Equal(health.TotalTicksExecuted, metrics.TotalTicksExecuted);
        Assert.Equal(health.TotalErrors, metrics.TotalErrors);
        Assert.Equal(health.TotalRetries, metrics.TotalRetries);
        Assert.Equal(health.TotalHeartbeatsEmitted, metrics.TotalHeartbeatsEmitted);
        Assert.Equal(health.Sessions.Count, metrics.Sessions.Count);
    }

    private static SessionHostOptions CreateOptions(bool enableAdminApi) =>
        new()
        {
            MaxGlobalParallelSessions = 2,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = enableAdminApi,
            AdminApiUrl = "http://127.0.0.1:0",
            Sessions =
            [
                TestOptionsFactory.Session("alpha", tickIntervalMs: 500, startupDelayMs: 0)
            ]
        };
}
