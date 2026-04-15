using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Memory;

namespace MultiSessionHost.Tests.Memory;

public sealed class InMemorySessionOperationalMemoryStoreTests
{
    [Fact]
    public async Task InitializeIfMissing_CreatesEmptySnapshot()
    {
        var sessionId = new SessionId("memory-init");
        var now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");
        var store = CreateStore();

        await store.InitializeIfMissingAsync(sessionId, now, CancellationToken.None);

        var snapshot = await store.GetAsync(sessionId, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(sessionId, snapshot!.SessionId);
        Assert.Equal(0, snapshot.Summary.KnownWorksiteCount);
    }

    [Fact]
    public async Task Upsert_IsIsolatedBySession()
    {
        var alpha = new SessionId("memory-alpha");
        var beta = new SessionId("memory-beta");
        var now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");
        var store = CreateStore();

        await store.UpsertAsync(alpha, SessionOperationalMemorySnapshot.Empty(alpha, now), [], CancellationToken.None);

        Assert.NotNull(await store.GetAsync(alpha, CancellationToken.None));
        Assert.Null(await store.GetAsync(beta, CancellationToken.None));
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task History_IsBounded()
    {
        var sessionId = new SessionId("memory-history");
        var now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");
        var store = CreateStore(maxHistoryEntries: 2);

        for (var index = 0; index < 4; index++)
        {
            var record = new MemoryObservationRecord(
                $"record-{index}",
                sessionId,
                MemoryObservationCategory.Worksite,
                $"worksite:{index}",
                now.AddSeconds(index),
                "test",
                $"record {index}",
                new Dictionary<string, string>());
            await store.UpsertAsync(sessionId, SessionOperationalMemorySnapshot.Empty(sessionId, now.AddSeconds(index)), [record], CancellationToken.None);
        }

        var history = await store.GetHistoryAsync(sessionId, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal("record-2", history[0].ObservationId);
        Assert.Equal("record-3", history[1].ObservationId);
    }

    private static InMemorySessionOperationalMemoryStore CreateStore(int maxHistoryEntries = 10) =>
        new(new SessionHostOptions
        {
            OperationalMemory = new OperationalMemoryOptions
            {
                MaxHistoryEntries = maxHistoryEntries
            }
        });
}
