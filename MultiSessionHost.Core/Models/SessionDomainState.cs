using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionDomainState(
    SessionId SessionId,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version,
    DomainSnapshotSource Source,
    NavigationState Navigation,
    CombatState Combat,
    ThreatState Threat,
    TargetState Target,
    CompanionState Companions,
    ResourceState Resources,
    LocationState Location,
    IReadOnlyList<string> Warnings)
{
    public static SessionDomainState CreateBootstrap(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            CapturedAtUtc: null,
            now,
            Version: 1,
            DomainSnapshotSource.Bootstrap,
            NavigationState.CreateDefault(),
            CombatState.CreateDefault(),
            ThreatState.CreateDefault(),
            TargetState.CreateDefault(),
            CompanionState.CreateDefault(),
            ResourceState.CreateDefault(),
            LocationState.CreateDefault(),
            Warnings: []);
}
