using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.State;

namespace MultiSessionHost.Tests.Domain;

public sealed class InMemorySessionDomainStateStoreTests
{
    [Fact]
    public async Task Store_InitializesAndReturnsSessionScopedState()
    {
        var store = new InMemorySessionDomainStateStore();
        var alpha = SessionDomainState.CreateBootstrap(new SessionId("domain-store-alpha"), DateTimeOffset.UtcNow);

        await store.InitializeAsync(alpha, CancellationToken.None);

        var found = await store.GetAsync(alpha.SessionId, CancellationToken.None);
        var missing = await store.GetAsync(new SessionId("domain-store-missing"), CancellationToken.None);

        Assert.Equal(alpha, found);
        Assert.Null(missing);
    }

    [Fact]
    public async Task UpdateAsync_OnlyAffectsTargetedSession()
    {
        var store = new InMemorySessionDomainStateStore();
        var alpha = SessionDomainState.CreateBootstrap(new SessionId("domain-update-alpha"), DateTimeOffset.UtcNow);
        var beta = SessionDomainState.CreateBootstrap(new SessionId("domain-update-beta"), DateTimeOffset.UtcNow);
        await store.InitializeAsync(alpha, CancellationToken.None);
        await store.InitializeAsync(beta, CancellationToken.None);

        await store.UpdateAsync(
            alpha.SessionId,
            current => current with
            {
                Version = current.Version + 1,
                Source = DomainSnapshotSource.UiProjection,
                Navigation = current.Navigation with { Status = NavigationStatus.Idle }
            },
            CancellationToken.None);

        var updatedAlpha = await store.GetAsync(alpha.SessionId, CancellationToken.None);
        var unchangedBeta = await store.GetAsync(beta.SessionId, CancellationToken.None);

        Assert.Equal(2, updatedAlpha!.Version);
        Assert.Equal(DomainSnapshotSource.UiProjection, updatedAlpha.Source);
        Assert.Equal(1, unchangedBeta!.Version);
        Assert.Equal(DomainSnapshotSource.Bootstrap, unchangedBeta.Source);
    }
}
