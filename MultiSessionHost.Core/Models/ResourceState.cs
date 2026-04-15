namespace MultiSessionHost.Core.Models;

public sealed record ResourceState(
    double? HealthPercent,
    double? CapacityPercent,
    double? EnergyPercent,
    int? AvailableChargeCount,
    int? CapacityCount,
    bool IsDegraded,
    bool IsCritical,
    DateTimeOffset? UpdatedAtUtc)
{
    public static ResourceState CreateDefault() =>
        new(
            HealthPercent: null,
            CapacityPercent: null,
            EnergyPercent: null,
            AvailableChargeCount: null,
            CapacityCount: null,
            IsDegraded: false,
            IsCritical: false,
            UpdatedAtUtc: null);
}
