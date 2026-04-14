using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionHealthSnapshot(
    SessionId SessionId,
    string DisplayName,
    SessionStatus CurrentStatus,
    SessionStatus DesiredStatus,
    SessionStatus ObservedStatus,
    DateTimeOffset? LastHeartbeatUtc,
    string? LastError,
    bool IsCircuitOpen,
    SessionMetricsSnapshot Metrics);
