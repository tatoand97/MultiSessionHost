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
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            7870,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(sessionId, enabled: false));

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

    private static Task WaitUntilRunningAsync(WorkerHostHarness harness, string sessionId) =>
        TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(new SessionId(sessionId))?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed session in time.");
}
