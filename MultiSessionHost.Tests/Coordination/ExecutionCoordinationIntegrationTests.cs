using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Coordination;

public sealed class ExecutionCoordinationIntegrationTests
{
    [Fact]
    public async Task UiCommandAndRefresh_ForSameSessionShareTheExecutionCoordinator()
    {
        const int basePort = 7940;
        const string alphaId = "coord-command-refresh-alpha";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);

        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(alphaId, tickIntervalMs: 60_000, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);
        var alphaSessionId = new SessionId(alphaId);

        await WaitForRunningAsync(harness, alphaSessionId);
        (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        await alphaApp.SetArtificialDelayAsync(350);
        var commandTask = client.PostAsJsonAsync(
            $"/sessions/{alphaId}/nodes/notesTextBox/text",
            new NodeTextCommandRequest("coordinated mutation", null));

        await WaitForActiveExecutionsAsync(client, alphaId, 1);

        var refreshTask = client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null);

        await WaitForWaitingExecutionsAsync(client, alphaId, 1);
        var waitingSnapshot = await client.GetFromJsonAsync<ExecutionCoordinationSnapshotDto>($"/coordination/sessions/{alphaId}");

        Assert.NotNull(waitingSnapshot);
        Assert.Contains(waitingSnapshot!.WaitingExecutions, execution => execution.OperationKind == "UiRefresh");

        await alphaApp.SetArtificialDelayAsync(0);

        using var commandResponse = await commandTask.WaitAsync(TimeSpan.FromSeconds(10));
        using var refreshResponse = await refreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        commandResponse.EnsureSuccessStatusCode();
        refreshResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TwoSessionsBoundToSameEffectiveTargetSerializeTargetFacingRefreshes()
    {
        const int port = 7950;
        const string sharedTargetSessionId = "coord-shared-target-app";
        const string alphaId = "coord-same-target-alpha";
        const string betaId = "coord-same-target-beta";

        await using var sharedApp = await TestDesktopAppProcessHost.StartAsync(sharedTargetSessionId, port);
        var options = CreateSharedSelfHostedOptions(
            port,
            sharedTargetSessionId,
            TestOptionsFactory.Session(alphaId, tickIntervalMs: 60_000, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, tickIntervalMs: 60_000, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitForRunningAsync(harness, new SessionId(alphaId), new SessionId(betaId));

        await sharedApp.SetArtificialDelayAsync(350);
        var alphaRefreshTask = client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null);

        await WaitForActiveExecutionsAsync(client, alphaId, 1);

        var betaRefreshTask = client.PostAsync($"/sessions/{betaId}/ui/refresh", content: null);

        await WaitForWaitingExecutionsAsync(client, betaId, 1);
        var snapshot = await client.GetFromJsonAsync<ExecutionCoordinationSnapshotDto>("/coordination");

        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.WaitingExecutions, execution => execution.SessionId == betaId && execution.OperationKind == "UiRefresh");

        await sharedApp.SetArtificialDelayAsync(0);

        using var alphaRefresh = await alphaRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        using var betaRefresh = await betaRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        alphaRefresh.EnsureSuccessStatusCode();
        betaRefresh.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TwoSessionsBoundToDifferentTargetsCanRefreshConcurrently()
    {
        const int basePort = 7960;
        const string alphaId = "coord-different-target-alpha";
        const string betaId = "coord-different-target-beta";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaId, basePort + 1);

        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(alphaId, tickIntervalMs: 60_000, startupDelayMs: 0),
            TestOptionsFactory.Session(betaId, tickIntervalMs: 60_000, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await WaitForRunningAsync(harness, new SessionId(alphaId), new SessionId(betaId));

        await alphaApp.SetArtificialDelayAsync(700);
        var alphaRefreshTask = client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null);

        await WaitForActiveExecutionsAsync(client, alphaId, 1);

        var stopwatch = Stopwatch.StartNew();
        using var betaRefresh = await client
            .PostAsync($"/sessions/{betaId}/ui/refresh", content: null)
            .WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        betaRefresh.EnsureSuccessStatusCode();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(650), $"Different target refresh was unexpectedly blocked for {stopwatch.Elapsed}.");

        await alphaApp.SetArtificialDelayAsync(0);
        using var alphaRefresh = await alphaRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        alphaRefresh.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task BindingChangeUpdatesCoordinationTargetKeyAndReattachesToNewTarget()
    {
        const int basePort = 7970;
        const string alphaId = "coord-rebind-alpha";
        const string betaTargetId = "coord-rebind-beta-target";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);
        await using var betaApp = await TestDesktopAppProcessHost.StartAsync(betaTargetId, basePort + 1);

        var options = CreateSharedSelfHostedOptions(
            basePort,
            alphaId,
            TestOptionsFactory.Session(alphaId, tickIntervalMs: 60_000, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);
        var sessionId = new SessionId(alphaId);

        await WaitForRunningAsync(harness, sessionId);
        (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).EnsureSuccessStatusCode();

        var keyBefore = ResolveTargetKey(harness, sessionId);

        using var upsertResponse = await client.PutAsJsonAsync(
            $"/bindings/{alphaId}",
            new SessionTargetBindingUpsertRequest(
                "shared-http",
                new Dictionary<string, string> { ["Port"] = (basePort + 1).ToString() },
                new DesktopTargetProfileOverrideDto(
                    ProcessName: null,
                    WindowTitleFragment: $"[SessionId: {betaTargetId}]",
                    CommandLineFragmentTemplate: $"--session-id {betaTargetId}",
                    BaseAddressTemplate: null,
                    MatchingMode: null,
                    Metadata: new Dictionary<string, string?>(),
                    SupportsUiSnapshots: null,
                    SupportsStateEndpoint: null)));
        upsertResponse.EnsureSuccessStatusCode();

        var keyAfter = ResolveTargetKey(harness, sessionId);
        Assert.NotEqual(keyBefore, keyAfter);

        await betaApp.SetArtificialDelayAsync(250);
        var refreshTask = client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null);

        await WaitForActiveExecutionsAsync(client, alphaId, 1);
        var snapshot = await client.GetFromJsonAsync<ExecutionCoordinationSnapshotDto>($"/coordination/sessions/{alphaId}");

        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.Resources, resource => resource.ResourceKey.Value == keyAfter);

        await betaApp.SetArtificialDelayAsync(0);
        using var refreshResponse = await refreshTask.WaitAsync(TimeSpan.FromSeconds(10));
        refreshResponse.EnsureSuccessStatusCode();

        var targetDto = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{alphaId}/target");
        Assert.NotNull(targetDto);
        Assert.NotNull(targetDto!.Attachment);
        Assert.Equal(betaApp.ProcessId, targetDto.Attachment!.ProcessId);
        Assert.Equal($"http://127.0.0.1:{basePort + 1}/", targetDto.Target.BaseAddress);

        _ = alphaApp;
    }

    private static async Task WaitForRunningAsync(WorkerHostHarness harness, params SessionId[] sessionIds)
    {
        await TestWait.UntilAsync(
            () => sessionIds.All(sessionId => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running),
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed sessions in time.");
    }

    private static async Task WaitForActiveExecutionsAsync(HttpClient client, string sessionId, int minimumCount)
    {
        await TestWait.UntilAsync(
            async () =>
            {
                var snapshot = await client.GetFromJsonAsync<ExecutionCoordinationSnapshotDto>($"/coordination/sessions/{sessionId}").ConfigureAwait(false);
                return snapshot is not null && snapshot.ActiveExecutions.Count >= minimumCount;
            },
            TimeSpan.FromSeconds(5),
            $"Coordination did not report {minimumCount} active execution(s) for session '{sessionId}' in time.");
    }

    private static async Task WaitForWaitingExecutionsAsync(HttpClient client, string sessionId, int minimumCount)
    {
        await TestWait.UntilAsync(
            async () =>
            {
                var snapshot = await client.GetFromJsonAsync<ExecutionCoordinationSnapshotDto>($"/coordination/sessions/{sessionId}").ConfigureAwait(false);
                return snapshot is not null && snapshot.WaitingExecutions.Count >= minimumCount;
            },
            TimeSpan.FromSeconds(5),
            $"Coordination did not report {minimumCount} waiting execution(s) for session '{sessionId}' in time.");
    }

    private static string ResolveTargetKey(WorkerHostHarness harness, SessionId sessionId)
    {
        var session = harness.Coordinator.GetSession(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var targetResolver = harness.Host.Services.GetRequiredService<IDesktopTargetProfileResolver>();
        var resourceResolver = harness.Host.Services.GetRequiredService<IExecutionResourceResolver>();
        var context = targetResolver.Resolve(session);
        return resourceResolver.CreateTargetIdentity(context).CanonicalKey;
    }

    private static SessionHostOptions CreateSharedSelfHostedOptions(
        int port,
        string sharedTargetSessionId,
        params SessionDefinitionOptions[] sessions) =>
        new()
        {
            MaxGlobalParallelSessions = sessions.Length,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets =
            [
                new DesktopTargetProfileOptions
                {
                    ProfileName = "shared-http",
                    Kind = DesktopTargetKind.SelfHostedHttpDesktop,
                    ProcessName = "MultiSessionHost.TestDesktopApp",
                    WindowTitleFragment = $"[SessionId: {sharedTargetSessionId}]",
                    CommandLineFragmentTemplate = $"--session-id {sharedTargetSessionId}",
                    BaseAddressTemplate = "http://127.0.0.1:{Port}/",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = true,
                    Metadata = new Dictionary<string, string?>
                    {
                        ["UiSource"] = "DesktopTestApp"
                    }
                }
            ],
            SessionTargetBindings = sessions
                .Select(session => TestOptionsFactory.SessionTargetBinding(session.SessionId, "shared-http", port.ToString()))
                .ToArray(),
            Sessions = sessions
        };
}
