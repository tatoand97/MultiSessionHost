using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record TargetState(
    bool HasActiveTarget,
    string? PrimaryTargetId,
    string? PrimaryTargetLabel,
    int? TrackedTargetCount,
    int? LockedTargetCount,
    int? SelectedTargetCount,
    TargetingStatus Status,
    DateTimeOffset? LastTargetChangedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static TargetState CreateDefault() =>
        new(
            HasActiveTarget: false,
            PrimaryTargetId: null,
            PrimaryTargetLabel: null,
            TrackedTargetCount: null,
            LockedTargetCount: null,
            SelectedTargetCount: null,
            TargetingStatus.None,
            LastTargetChangedAtUtc: null,
            UpdatedAtUtc: null);
}
