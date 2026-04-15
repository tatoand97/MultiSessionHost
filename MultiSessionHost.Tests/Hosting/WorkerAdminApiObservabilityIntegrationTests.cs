using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.AdminDesktop.Api;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiObservabilityIntegrationTests
{
    [Fact]
    public async Task ObservabilityEndpointsReturnRecordedSessionData()
    {
        var options = CreateOptions();

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);
        var store = harness.Host.Services.GetRequiredService<ISessionObservabilityStore>();
        var sessionId = new SessionId("alpha");

        await store.RecordAsync(
            new SessionLatencyMeasurement(
                sessionId,
                Guid.NewGuid(),
                "ui.snapshot",
                nameof(SessionObservabilityCategory.Snapshot),
                DateTimeOffset.UtcNow,
                12,
                SessionObservabilityOutcome.Success.ToString(),
                "snapshot-ok",
                "snapshot-ok",
                nameof(WorkerAdminApiObservabilityIntegrationTests),
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        await store.RecordErrorAsync(
            new AdapterErrorRecord(
                sessionId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                "Win32Adapter",
                "attach",
                typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException),
                "attach failed",
                "adapter-failure",
                nameof(WorkerAdminApiObservabilityIntegrationTests),
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        var global = await client.GetFromJsonAsync<GlobalObservabilitySnapshotDto>("/observability");
        var session = await client.GetFromJsonAsync<SessionObservabilityDto>("/sessions/alpha/observability");
        var events = await client.GetFromJsonAsync<SessionObservabilityEventDto[]>("/sessions/alpha/observability/events");
        var metrics = await client.GetFromJsonAsync<SessionObservabilityMetricsDto>("/sessions/alpha/observability/metrics");
        var errors = await client.GetFromJsonAsync<AdapterErrorRecordDto[]>("/sessions/alpha/observability/errors");

        Assert.NotNull(global);
        Assert.NotNull(session);
        Assert.NotNull(events);
        Assert.NotNull(metrics);
        Assert.NotNull(errors);
        Assert.Single(events!);
        Assert.Single(errors!);
        Assert.Equal("alpha", session!.Summary.SessionId);
        Assert.Equal(1, session.Summary.SnapshotCount);
        Assert.Equal(1, session.Summary.AdapterErrorCount);
        Assert.Equal("alpha", metrics!.Summary.SessionId);
        Assert.Single(metrics.RecentLatencies);
        Assert.Equal(1, global!.TotalErrors);
        Assert.True(global.TotalEvents >= 2);

        var typedClient = new AdminApiClient(client);
        var typedGlobal = await typedClient.GetObservabilityAsync();
        var typedSession = await typedClient.GetSessionObservabilityAsync("alpha");

        Assert.Equal(global.TotalEvents, typedGlobal.TotalEvents);
        Assert.NotNull(typedSession);
        Assert.Equal(session.Summary.SessionId, typedSession!.Summary.SessionId);
    }

    private static SessionHostOptions CreateOptions() =>
        new()
        {
            MaxGlobalParallelSessions = 2,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            Sessions =
            [
                TestOptionsFactory.Session("alpha", tickIntervalMs: 500, startupDelayMs: 0)
            ]
        };
}