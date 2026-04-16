using System.Net.Http.Json;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiRecoveryIntegrationTests
{
    [Fact]
    public async Task RecoveryEndpoints_ExposeTheCurrentRecoverySnapshotAndHistory()
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
            RuntimePersistence = new RuntimePersistenceOptions { EnableRuntimePersistence = false },
            Sessions = [TestOptionsFactory.Session("alpha", tickIntervalMs: 500, startupDelayMs: 0)]
        };

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = harness.Client ?? throw new InvalidOperationException("The admin API client was not initialized.");

        var recovery = await client.GetFromJsonAsync<SessionRecoverySnapshotDto[]>("/recovery");
        var sessionRecovery = await client.GetFromJsonAsync<SessionRecoverySnapshotDto>("/sessions/alpha/recovery");
        var history = await client.GetFromJsonAsync<SessionRecoveryHistoryEntryDto[]>("/sessions/alpha/recovery/history");

        Assert.NotNull(recovery);
        Assert.Single(recovery!);
        Assert.Equal("alpha", recovery[0].SessionId);
        Assert.NotNull(sessionRecovery);
        Assert.Equal("alpha", sessionRecovery!.SessionId);
        Assert.Equal("Healthy", sessionRecovery.RecoveryStatus);
        Assert.Equal("Closed", sessionRecovery.CircuitBreakerState);
        Assert.NotNull(history);
        Assert.Empty(history!);
    }
}