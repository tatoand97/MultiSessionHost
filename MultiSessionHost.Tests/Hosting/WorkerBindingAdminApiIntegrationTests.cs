using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerBindingAdminApiIntegrationTests
{
    [Fact]
    public async Task BindingsEndpoints_ListUpsertPersistGetAndDeleteRuntimeBindings()
    {
        var persistenceDirectory = Path.Combine(Path.GetTempPath(), "msh-tests", Guid.NewGuid().ToString("N"));
        var persistencePath = Path.Combine(persistenceDirectory, "bindings.json");
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
            BindingStoreFilePath = persistencePath,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", enabled: false, startupDelayMs: 0)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var snapshot = await client.GetFromJsonAsync<BindingStoreSnapshotDto>("/bindings");

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Bindings);
        Assert.Equal("7100", snapshot.Bindings.Single().Variables["Port"]);

        var upsertRequest = new SessionTargetBindingUpsertRequest(
            "test-app",
            new Dictionary<string, string> { ["Port"] = "7200" },
            Overrides: null);
        var putResponse = await client.PutAsJsonAsync("/bindings/alpha", upsertRequest);
        putResponse.EnsureSuccessStatusCode();
        var updated = await putResponse.Content.ReadFromJsonAsync<SessionTargetBindingDto>();

        Assert.NotNull(updated);
        Assert.Equal("7200", updated!.Variables["Port"]);
        Assert.True(File.Exists(persistencePath));
        var persistedJson = await File.ReadAllTextAsync(persistencePath);
        Assert.Contains("\"port\": \"7200\"", persistedJson, StringComparison.OrdinalIgnoreCase);

        var fetched = await client.GetFromJsonAsync<SessionTargetBindingDto>("/bindings/alpha");

        Assert.NotNull(fetched);
        Assert.Equal("7200", fetched!.Variables["Port"]);

        var deleteResponse = await client.DeleteAsync("/bindings/alpha");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedBindingResponse = await client.GetAsync("/bindings/alpha");
        var deletedTargetResponse = await client.GetAsync("/sessions/alpha/target");

        Assert.Equal(HttpStatusCode.NotFound, deletedBindingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deletedTargetResponse.StatusCode);
    }

    [Fact]
    public async Task SessionsTargetReflectsRuntimeBindingChangesWithoutRestart()
    {
        const string workerSessionId = "alpha";
        const int firstPort = 7920;
        const int secondPort = 7921;

        TestDesktopAppProcessHost? firstApp = null;
        TestDesktopAppProcessHost? secondApp = null;

        try
        {
            firstApp = await TestDesktopAppProcessHost.StartAsync(workerSessionId, firstPort);

            var options = new SessionHostOptions
            {
                DriverMode = DriverMode.DesktopTargetAdapter,
                EnableUiSnapshots = true,
                EnableAdminApi = true,
                AdminApiUrl = "http://127.0.0.1:0",
                DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
                SessionTargetBindings =
                [
                    TestOptionsFactory.SessionTargetBinding(workerSessionId, "test-app", firstPort.ToString())
                ],
                Sessions = [TestOptionsFactory.Session(workerSessionId, startupDelayMs: 0)]
            };

            await using var harness = await WorkerHostHarness.StartAsync(options);
            var client = Assert.IsType<HttpClient>(harness.Client);

            await TestWait.UntilAsync(
                () => harness.Coordinator.GetSession(new SessionId(workerSessionId))?.Runtime.CurrentStatus == SessionStatus.Running,
                TimeSpan.FromSeconds(10),
                "The worker runtime did not start the rebound desktop session in time.");

            var initialRefresh = await client.PostAsync($"/sessions/{workerSessionId}/ui/refresh", content: null);
            initialRefresh.EnsureSuccessStatusCode();

            var initialTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(initialTarget);
            Assert.NotNull(initialTarget!.Attachment);
            Assert.Equal(firstApp.ProcessId, initialTarget.Attachment!.ProcessId);
            Assert.Equal($"http://127.0.0.1:{firstPort}/", initialTarget.Target.BaseAddress);

            await firstApp.DisposeAsync();
            firstApp = null;
            secondApp = await TestDesktopAppProcessHost.StartAsync(workerSessionId, secondPort);

            var updateResponse = await client.PutAsJsonAsync(
                $"/bindings/{workerSessionId}",
                new SessionTargetBindingUpsertRequest(
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = secondPort.ToString() },
                    Overrides: null));
            updateResponse.EnsureSuccessStatusCode();

            var updatedTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(updatedTarget);
            Assert.Equal($"http://127.0.0.1:{secondPort}/", updatedTarget!.Target.BaseAddress);
            Assert.Null(updatedTarget.Attachment);

            var refreshResponse = await client.PostAsync($"/sessions/{workerSessionId}/ui/refresh", content: null);
            refreshResponse.EnsureSuccessStatusCode();
            var reboundTarget = await client.GetFromJsonAsync<SessionTargetDto>($"/sessions/{workerSessionId}/target");

            Assert.NotNull(reboundTarget);
            Assert.NotNull(reboundTarget!.Attachment);
            Assert.Equal(secondApp.ProcessId, reboundTarget.Attachment!.ProcessId);
            Assert.Equal($"http://127.0.0.1:{secondPort}/", reboundTarget.Target.BaseAddress);
        }
        finally
        {
            if (secondApp is not null)
            {
                await secondApp.DisposeAsync();
            }

            if (firstApp is not null)
            {
                await firstApp.DisposeAsync();
            }
        }
    }
}
