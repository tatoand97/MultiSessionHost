using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionHeartbeat(
    SessionId SessionId,
    DateTimeOffset TimestampUtc,
    SessionStatus ObservedStatus);
