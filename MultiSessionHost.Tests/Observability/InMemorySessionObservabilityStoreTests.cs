using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Observability;

namespace MultiSessionHost.Tests.Observability;

public sealed class InMemorySessionObservabilityStoreTests
{
    [Fact]
    public async Task RecordAsync_KeepsTheMostRecentEventsAndTracksSummary()
    {
        var store = new InMemorySessionObservabilityStore(new SessionHostOptions
        {
            Observability = new ObservabilityOptions
            {
                MaxEventsPerSession = 2,
                MaxErrorsPerSession = 2,
                MaxReasonMetricsPerSession = 2
            }
        });

        var sessionId = new SessionId("alpha");
        await store.RecordAsync(CreateLatency(sessionId, "ui.snapshot", "Snapshot", "reason-1"), CancellationToken.None);
        await store.RecordAsync(CreateLatency(sessionId, "ui.extraction", "Extraction", "reason-2"), CancellationToken.None);
        await store.RecordAsync(CreateLatency(sessionId, "ui.domain", "Domain", "reason-3"), CancellationToken.None);

        var snapshot = await store.GetAsync(sessionId, CancellationToken.None);
        var metrics = await store.GetMetricsAsync(sessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.NotNull(metrics);
        Assert.Equal(2, snapshot!.RecentEvents.Count);
        Assert.Equal("ui.extraction", snapshot.RecentEvents[0].EventType);
        Assert.Equal("ui.domain", snapshot.RecentEvents[1].EventType);
        Assert.Equal(1, snapshot.Summary.DomainProjectionCount);
        Assert.Equal(1, snapshot.Summary.SnapshotCount);
        Assert.Equal(1, snapshot.Summary.ExtractionCount);
        Assert.Equal(3, snapshot.Summary.ReasonCounts.Count);
        Assert.Equal(2, metrics!.RecentLatencies.Count);
    }

    [Fact]
    public async Task RecordErrorAsync_TracksErrorsAndDegradesTheSummary()
    {
        var store = new InMemorySessionObservabilityStore(new SessionHostOptions());
        var sessionId = new SessionId("beta");

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
                nameof(InMemorySessionObservabilityStoreTests),
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        var snapshot = await store.GetAsync(sessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(SessionObservabilityStatus.Degraded, snapshot!.Summary.Status);
        Assert.Equal(1, snapshot.Summary.AdapterErrorCount);
        Assert.Single(snapshot.RecentErrors);
    }

    private static SessionLatencyMeasurement CreateLatency(SessionId sessionId, string eventType, string category, string reasonCode) =>
        new(
            sessionId,
            Guid.NewGuid(),
            eventType,
            category,
            DateTimeOffset.UtcNow,
            25,
            SessionObservabilityOutcome.Success.ToString(),
            reasonCode,
            reasonCode,
            nameof(InMemorySessionObservabilityStoreTests),
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal));
}