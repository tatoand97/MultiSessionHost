using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record CompanionState(
    CompanionStatus Status,
    bool? AreAvailable,
    bool? AreHealthy,
    int? ActiveCount,
    int? DeployedCount,
    int? DockedCount,
    int? IdleCount,
    DateTimeOffset? UpdatedAtUtc)
{
    public static CompanionState CreateDefault() =>
        new(
            CompanionStatus.Unknown,
            AreAvailable: null,
            AreHealthy: null,
            ActiveCount: null,
            DeployedCount: null,
            DockedCount: null,
            IdleCount: null,
            UpdatedAtUtc: null);
}
