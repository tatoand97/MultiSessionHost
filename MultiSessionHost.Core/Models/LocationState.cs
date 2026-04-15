using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record LocationState(
    string? ContextLabel,
    string? SubLocationLabel,
    bool? IsBaseOrHome,
    bool IsUnknown,
    LocationConfidence Confidence,
    DateTimeOffset? ArrivedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static LocationState CreateDefault() =>
        new(
            ContextLabel: null,
            SubLocationLabel: null,
            IsBaseOrHome: null,
            IsUnknown: true,
            LocationConfidence.Unknown,
            ArrivedAtUtc: null,
            UpdatedAtUtc: null);
}
