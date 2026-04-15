using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Extensions;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiUiIntegrationTests
{
    [Fact]
    public async Task UiEndpoints_ReturnProjectedTreeRawSnapshotAndRefreshStatusAgainstTheRealWorkerRuntime()
    {
        const int basePort = 7810;
        const string alphaId = "api-ui-alpha";
        const string betaId = "api-ui-beta";

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
            "The worker runtime did not start the desktop-backed sessions in time.");

        var refreshAlpha = await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null);
        refreshAlpha.EnsureSuccessStatusCode();
        var refreshDto = await refreshAlpha.Content.ReadFromJsonAsync<SessionUiRefreshDto>();
        var treeDto = await client.GetFromJsonAsync<SessionUiDto>($"/sessions/{alphaId}/ui");
        var rawDto = await client.GetFromJsonAsync<SessionUiRawDto>($"/sessions/{alphaId}/ui/raw");
        var targetDto = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{alphaId}/target");
        var targetsDto = await client.GetFromJsonAsync<DesktopTargetProfileDto[]>("/targets");
        var profileDto = await client.GetFromJsonAsync<DesktopTargetProfileDto>("/targets/test-app");

        Assert.NotNull(refreshDto);
        Assert.NotNull(treeDto);
        Assert.NotNull(rawDto);
        Assert.NotNull(targetDto);
        Assert.NotNull(targetsDto);
        Assert.NotNull(profileDto);
        Assert.NotNull(refreshDto!.Tree);
        Assert.NotNull(refreshDto.RawSnapshot);
        Assert.NotNull(refreshDto.LastRefreshRequestedAtUtc);
        Assert.NotNull(refreshDto.LastRefreshCompletedAtUtc);
        Assert.Equal(alphaId, treeDto!.SessionId);
        Assert.Equal(alphaId, treeDto.Tree!.Metadata.SessionId);
        Assert.Equal(alphaId, rawDto!.RawSnapshot!.Value.GetProperty("sessionId").GetString());
        Assert.Equal(alphaId, refreshDto.RawSnapshot!.Value.GetProperty("sessionId").GetString());
        Assert.Equal(alphaId, targetDto!.SessionId);
        Assert.Equal("test-app", targetDto.Profile.ProfileName);
        Assert.Equal("DesktopTestApp", targetDto.AdapterKind);
        Assert.Equal(basePort.ToString(), targetDto.Binding.Variables["Port"]);
        Assert.NotNull(targetDto.Attachment);
        Assert.Equal(alphaApp.ProcessId, targetDto.Attachment!.ProcessId);
        Assert.Equal($"http://127.0.0.1:{basePort}/", targetDto.Target.BaseAddress);
        Assert.Single(targetsDto!);
        Assert.Equal("test-app", profileDto!.ProfileName);
    }

    [Fact]
    public async Task UiEndpoints_MaintainIsolationAcrossSessions()
    {
        const int basePort = 7820;
        const string alphaId = "api-ui-isolation-alpha";
        const string betaId = "api-ui-isolation-beta";

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
            "The worker runtime did not start the desktop-backed sessions in time.");

        var alphaRefresh = await (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).Content.ReadFromJsonAsync<SessionUiRefreshDto>();
        var betaRefresh = await (await client.PostAsync($"/sessions/{betaId}/ui/refresh", content: null)).Content.ReadFromJsonAsync<SessionUiRefreshDto>();

        Assert.NotNull(alphaRefresh);
        Assert.NotNull(betaRefresh);
        Assert.Equal(alphaId, alphaRefresh!.Tree!.Metadata.SessionId);
        Assert.Equal(betaId, betaRefresh!.Tree!.Metadata.SessionId);
        Assert.Equal(alphaId, alphaRefresh.RawSnapshot!.Value.GetProperty("sessionId").GetString());
        Assert.Equal(betaId, betaRefresh.RawSnapshot!.Value.GetProperty("sessionId").GetString());

        var alphaTexts = alphaRefresh.Tree.Flatten().Select(static node => node.Text).Where(static text => !string.IsNullOrWhiteSpace(text)).ToArray();
        var betaTexts = betaRefresh.Tree.Flatten().Select(static node => node.Text).Where(static text => !string.IsNullOrWhiteSpace(text)).ToArray();

        Assert.Contains($"Notes for {alphaId}", alphaTexts);
        Assert.Contains($"Notes for {betaId}", betaTexts);
        Assert.DoesNotContain($"Notes for {betaId}", alphaTexts);
        Assert.DoesNotContain($"Notes for {alphaId}", betaTexts);
    }
}
