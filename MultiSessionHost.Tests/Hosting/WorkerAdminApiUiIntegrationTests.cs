using System.Net;
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

    [Fact]
    public async Task CommandEndpoints_ExecuteSemanticCommandsAndRefreshUiAgainstTheRealWorkerRuntime()
    {
        const int basePort = 7830;
        const string alphaId = "api-ui-command-alpha";

        await using var alphaApp = await TestDesktopAppProcessHost.StartAsync(alphaId, basePort);

        var options = TestOptionsFactory.CreateDesktopTestAppOptions(
            basePort,
            true,
            "http://127.0.0.1:0",
            TestOptionsFactory.Session(alphaId, tickIntervalMs: 60_000, startupDelayMs: 0));

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(new SessionId(alphaId))?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the desktop-backed session in time.");

        var invokeResponse = await client.PostAsJsonAsync(
            $"/sessions/{alphaId}/commands",
            new UiCommandRequest("tickButton", "InvokeNodeAction", "Tick", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, invokeResponse.StatusCode);
        var invokeResult = await invokeResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.NotNull(invokeResult);
        Assert.True(invokeResult!.Succeeded);
        Assert.True(invokeResult.UpdatedUiStateAvailable);

        var clickResponse = await client.PostAsync($"/sessions/{alphaId}/nodes/startButton/click", content: null);
        var clickResult = await clickResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.Equal(HttpStatusCode.OK, clickResponse.StatusCode);
        Assert.NotNull(clickResult);
        Assert.True(clickResult!.Succeeded);

        var textResponse = await client.PostAsJsonAsync(
            $"/sessions/{alphaId}/nodes/notesTextBox/text",
            new NodeTextCommandRequest("Updated notes for alpha", null));
        var textResult = await textResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.Equal(HttpStatusCode.OK, textResponse.StatusCode);
        Assert.NotNull(textResult);
        Assert.True(textResult!.Succeeded);

        var toggleResponse = await client.PostAsJsonAsync(
            $"/sessions/{alphaId}/nodes/enabledCheckBox/toggle",
            new NodeToggleCommandRequest(false, null));
        var toggleResult = await toggleResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.Equal(HttpStatusCode.OK, toggleResponse.StatusCode);
        Assert.NotNull(toggleResult);
        Assert.True(toggleResult!.Succeeded);

        var selectResponse = await client.PostAsJsonAsync(
            $"/sessions/{alphaId}/nodes/itemsListBox/select",
            new NodeSelectCommandRequest($"{alphaId}-item-2", null));
        var selectResult = await selectResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.Equal(HttpStatusCode.OK, selectResponse.StatusCode);
        Assert.NotNull(selectResult);
        Assert.True(selectResult!.Succeeded);

        var state = await alphaApp.GetStateAsync();
        Assert.Equal("Running", state.Status);
        Assert.Equal("Updated notes for alpha", state.Notes);
        Assert.False(state.Enabled);
        Assert.Equal($"{alphaId}-item-2", state.SelectedItem);
        Assert.Equal(1, state.TickCount);

        var uiDto = await client.GetFromJsonAsync<SessionUiDto>($"/sessions/{alphaId}/ui");
        Assert.NotNull(uiDto);
        Assert.NotNull(uiDto!.Tree);
        var notesNode = uiDto.Tree!.Flatten().Single(node => string.Equals(node.Name, "notesTextBox", StringComparison.Ordinal));
        var enabledNode = uiDto.Tree.Flatten().Single(node => string.Equals(node.Name, "enabledCheckBox", StringComparison.Ordinal));
        Assert.Equal("Updated notes for alpha", notesNode.Text);
        Assert.False(enabledNode.Selected);

        var listBoxNode = uiDto.Tree.Flatten().Single(node => string.Equals(node.Name, "itemsListBox", StringComparison.Ordinal));
        Assert.Equal($"{alphaId}-item-2", listBoxNode.Attributes.Single(attribute => attribute.Name == "selectedItem").Value);
        Assert.Equal("1", uiDto.Tree.Metadata.Properties["tickCount"]);
    }

    [Fact]
    public async Task CommandEndpoints_MaintainIsolationAcrossSessions()
    {
        const int basePort = 7840;
        const string alphaId = "api-ui-command-isolation-alpha";
        const string betaId = "api-ui-command-isolation-beta";

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

        await (await client.PostAsync($"/sessions/{alphaId}/ui/refresh", content: null)).Content.ReadFromJsonAsync<SessionUiRefreshDto>();
        await (await client.PostAsync($"/sessions/{betaId}/ui/refresh", content: null)).Content.ReadFromJsonAsync<SessionUiRefreshDto>();

        var commandResponse = await client.PostAsJsonAsync(
            $"/sessions/{alphaId}/commands",
            new UiCommandRequest("notesTextBox", "SetText", null, "Alpha only mutation", null, null, null));
        var commandResult = await commandResponse.Content.ReadFromJsonAsync<UiCommandResultDto>();
        Assert.Equal(HttpStatusCode.OK, commandResponse.StatusCode);
        Assert.NotNull(commandResult);
        Assert.True(commandResult!.Succeeded);

        var alphaState = await alphaApp.GetStateAsync();
        var betaState = await betaApp.GetStateAsync();

        Assert.Equal("Alpha only mutation", alphaState.Notes);
        Assert.Equal($"Notes for {betaId}", betaState.Notes);

        var alphaUi = await client.GetFromJsonAsync<SessionUiDto>($"/sessions/{alphaId}/ui");
        var betaUi = await client.GetFromJsonAsync<SessionUiDto>($"/sessions/{betaId}/ui");

        Assert.NotNull(alphaUi);
        Assert.NotNull(betaUi);
        Assert.Equal(
            "Alpha only mutation",
            alphaUi!.Tree!.Flatten().Single(node => string.Equals(node.Name, "notesTextBox", StringComparison.Ordinal)).Text);
        Assert.Equal(
            $"Notes for {betaId}",
            betaUi!.Tree!.Flatten().Single(node => string.Equals(node.Name, "notesTextBox", StringComparison.Ordinal)).Text);
    }
}
