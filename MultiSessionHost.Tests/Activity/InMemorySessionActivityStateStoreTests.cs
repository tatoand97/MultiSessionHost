using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;

namespace MultiSessionHost.Tests.Activity;

public class InMemorySessionActivityStateStoreTests
{
    private readonly InMemorySessionActivityStateStore _store = new();

    [Fact]
    public async Task InitializeAsync_NewSession_Succeeds()
    {
        var sessionId = new SessionId("test-1");
        var snapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, DateTimeOffset.UtcNow);

        await _store.InitializeAsync(sessionId, snapshot, CancellationToken.None);

        var retrieved = await _store.GetAsync(sessionId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(sessionId, retrieved.SessionId);
        Assert.Equal(SessionActivityStateKind.Idle, retrieved.CurrentState);
    }

    [Fact]
    public async Task InitializeAsync_AlreadyInitialized_Throws()
    {
        var sessionId = new SessionId("test-2");
        var snapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, DateTimeOffset.UtcNow);

        await _store.InitializeAsync(sessionId, snapshot, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _store.InitializeAsync(sessionId, snapshot, CancellationToken.None));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingSnapshot()
    {
        var sessionId = new SessionId("test-3");
        var snapshot1 = SessionActivitySnapshot.CreateBootstrap(sessionId, DateTimeOffset.UtcNow);
        await _store.InitializeAsync(sessionId, snapshot1, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var transition = new SessionActivityTransition(
            SessionActivityStateKind.Idle,
            SessionActivityStateKind.Traveling,
            "navigation-in-progress",
            "Navigation started",
            now,
            new Dictionary<string, string> { { "destination", "Market" } });

        var snapshot2 = InMemorySessionActivityStateStore.AppendTransition(snapshot1, transition);
        await _store.UpsertAsync(sessionId, snapshot2, CancellationToken.None);

        var retrieved = await _store.GetAsync(sessionId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(SessionActivityStateKind.Traveling, retrieved.CurrentState);
        Assert.Single(retrieved.History);
    }

    [Fact]
    public async Task GetAsync_NonexistentSession_ReturnsNull()
    {
        var sessionId = new SessionId("nonexistent");
        var retrieved = await _store.GetAsync(sessionId, CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSnapshots()
    {
        var sessionId1 = new SessionId("test-4");
        var sessionId2 = new SessionId("test-5");
        var now = DateTimeOffset.UtcNow;

        var snapshot1 = SessionActivitySnapshot.CreateBootstrap(sessionId1, now);
        var snapshot2 = SessionActivitySnapshot.CreateBootstrap(sessionId2, now);

        await _store.InitializeAsync(sessionId1, snapshot1, CancellationToken.None);
        await _store.InitializeAsync(sessionId2, snapshot2, CancellationToken.None);

        var all = await _store.GetAllAsync(CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.SessionId == sessionId1);
        Assert.Contains(all, s => s.SessionId == sessionId2);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsHistoryForSession()
    {
        var sessionId = new SessionId("test-6");
        var now = DateTimeOffset.UtcNow;
        var snapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, now);
        await _store.InitializeAsync(sessionId, snapshot, CancellationToken.None);

        var transition1 = new SessionActivityTransition(
            SessionActivityStateKind.Idle,
            SessionActivityStateKind.Traveling,
            "navigation-start",
            "Started traveling",
            now.AddSeconds(1),
            new Dictionary<string, string>());
        var snapshot2 = InMemorySessionActivityStateStore.AppendTransition(snapshot, transition1);

        var transition2 = new SessionActivityTransition(
            SessionActivityStateKind.Traveling,
            SessionActivityStateKind.Arriving,
            "navigation-end",
            "Arrived at destination",
            now.AddSeconds(2),
            new Dictionary<string, string>());
        var snapshot3 = InMemorySessionActivityStateStore.AppendTransition(snapshot2, transition2);

        await _store.UpsertAsync(sessionId, snapshot3, CancellationToken.None);

        var history = await _store.GetHistoryAsync(sessionId, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(SessionActivityStateKind.Idle, history[0].FromState);
        Assert.Equal(SessionActivityStateKind.Traveling, history[0].ToState);
        Assert.Equal(SessionActivityStateKind.Traveling, history[1].FromState);
        Assert.Equal(SessionActivityStateKind.Arriving, history[1].ToState);
    }

    [Fact]
    public async Task GetHistoryAsync_NonexistentSession_ReturnsEmpty()
    {
        var sessionId = new SessionId("nonexistent");
        var history = await _store.GetHistoryAsync(sessionId, CancellationToken.None);
        Assert.Empty(history);
    }

    [Fact]
    public async Task RemoveAsync_RemovesSession()
    {
        var sessionId = new SessionId("test-7");
        var snapshot = SessionActivitySnapshot.CreateBootstrap(sessionId, DateTimeOffset.UtcNow);
        await _store.InitializeAsync(sessionId, snapshot, CancellationToken.None);

        await _store.RemoveAsync(sessionId, CancellationToken.None);

        var retrieved = await _store.GetAsync(sessionId, CancellationToken.None);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task IsThreadSafe_MultipleSessionsIsolated()
    {
        var sessionId1 = new SessionId("test-8");
        var sessionId2 = new SessionId("test-9");
        var now = DateTimeOffset.UtcNow;

        var snapshot1 = SessionActivitySnapshot.CreateBootstrap(sessionId1, now);
        var snapshot2 = SessionActivitySnapshot.CreateBootstrap(sessionId2, now);

        var task1 = _store.InitializeAsync(sessionId1, snapshot1, CancellationToken.None);
        var task2 = _store.InitializeAsync(sessionId2, snapshot2, CancellationToken.None);

        await Task.WhenAll(task1.AsTask(), task2.AsTask());

        var retrieved1 = await _store.GetAsync(sessionId1, CancellationToken.None);
        var retrieved2 = await _store.GetAsync(sessionId2, CancellationToken.None);

        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal(sessionId1, retrieved1.SessionId);
        Assert.Equal(sessionId2, retrieved2.SessionId);
    }
}
