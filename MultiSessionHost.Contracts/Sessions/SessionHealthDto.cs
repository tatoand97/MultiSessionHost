namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionHealthDto(
    string SessionId,
    string DisplayName,
    string CurrentStatus,
    string DesiredStatus,
    string ObservedStatus,
    DateTimeOffset? LastHeartbeatUtc,
    string? LastError,
    bool IsCircuitOpen,
    SessionMetricsDto Metrics);
