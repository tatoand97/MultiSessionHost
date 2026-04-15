namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionDomainStateDto(
    string SessionId,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version,
    string Source,
    NavigationStateDto Navigation,
    CombatStateDto Combat,
    ThreatStateDto Threat,
    TargetStateDto Target,
    CompanionStateDto Companions,
    ResourceStateDto Resources,
    LocationStateDto Location,
    IReadOnlyList<string> Warnings);

public sealed record NavigationStateDto(
    string Status,
    bool IsTransitioning,
    string? DestinationLabel,
    string? RouteLabel,
    double? ProgressPercent,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CombatStateDto(
    string Status,
    string? ActivityPhase,
    bool OffensiveActionsActive,
    bool DefensivePostureActive,
    DateTimeOffset? EngagedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record ThreatStateDto(
    string Severity,
    int? UnknownCount,
    int? NeutralCount,
    int? HostileCount,
    bool? IsSafe,
    DateTimeOffset? LastThreatChangedAtUtc,
    IReadOnlyList<string> Signals);

public sealed record TargetStateDto(
    bool HasActiveTarget,
    string? PrimaryTargetId,
    string? PrimaryTargetLabel,
    int? TrackedTargetCount,
    int? LockedTargetCount,
    int? SelectedTargetCount,
    string Status,
    DateTimeOffset? LastTargetChangedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CompanionStateDto(
    string Status,
    bool? AreAvailable,
    bool? AreHealthy,
    int? ActiveCount,
    int? DeployedCount,
    int? DockedCount,
    int? IdleCount,
    DateTimeOffset? UpdatedAtUtc);

public sealed record ResourceStateDto(
    double? HealthPercent,
    double? CapacityPercent,
    double? EnergyPercent,
    int? AvailableChargeCount,
    int? CapacityCount,
    bool IsDegraded,
    bool IsCritical,
    DateTimeOffset? UpdatedAtUtc);

public sealed record LocationStateDto(
    string? ContextLabel,
    string? SubLocationLabel,
    bool? IsBaseOrHome,
    bool IsUnknown,
    string Confidence,
    DateTimeOffset? ArrivedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
