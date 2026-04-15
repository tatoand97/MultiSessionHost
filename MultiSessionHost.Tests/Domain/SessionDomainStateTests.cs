using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Tests.Domain;

public sealed class SessionDomainStateTests
{
    [Fact]
    public void CreateBootstrap_InitializesSaneGenericDefaults()
    {
        var now = DateTimeOffset.Parse("2026-04-15T12:00:00Z");
        var state = SessionDomainState.CreateBootstrap(new SessionId("domain-defaults"), now);

        Assert.Equal("domain-defaults", state.SessionId.Value);
        Assert.Null(state.CapturedAtUtc);
        Assert.Equal(now, state.UpdatedAtUtc);
        Assert.Equal(1, state.Version);
        Assert.Equal(DomainSnapshotSource.Bootstrap, state.Source);
        Assert.Equal(NavigationStatus.Unknown, state.Navigation.Status);
        Assert.Equal(CombatStatus.Idle, state.Combat.Status);
        Assert.Equal(ThreatSeverity.Unknown, state.Threat.Severity);
        Assert.Equal(TargetingStatus.None, state.Target.Status);
        Assert.False(state.Target.HasActiveTarget);
        Assert.Equal(CompanionStatus.Unknown, state.Companions.Status);
        Assert.False(state.Resources.IsDegraded);
        Assert.False(state.Resources.IsCritical);
        Assert.True(state.Location.IsUnknown);
        Assert.Empty(state.Warnings);
    }
}
