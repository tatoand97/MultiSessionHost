using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record CombatState(
    CombatStatus Status,
    string? ActivityPhase,
    bool OffensiveActionsActive,
    bool DefensivePostureActive,
    DateTimeOffset? EngagedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static CombatState CreateDefault() =>
        new(
            CombatStatus.Idle,
            ActivityPhase: null,
            OffensiveActionsActive: false,
            DefensivePostureActive: false,
            EngagedAtUtc: null,
            UpdatedAtUtc: null);
}
