using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record NavigationState(
    NavigationStatus Status,
    bool IsTransitioning,
    string? DestinationLabel,
    string? RouteLabel,
    double? ProgressPercent,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static NavigationState CreateDefault() =>
        new(
            NavigationStatus.Unknown,
            IsTransitioning: false,
            DestinationLabel: null,
            RouteLabel: null,
            ProgressPercent: null,
            StartedAtUtc: null,
            UpdatedAtUtc: null);
}
