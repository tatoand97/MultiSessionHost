using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record ThreatState(
    ThreatSeverity Severity,
    int? UnknownCount,
    int? NeutralCount,
    int? HostileCount,
    bool? IsSafe,
    DateTimeOffset? LastThreatChangedAtUtc,
    IReadOnlyList<string> Signals)
{
    public static ThreatState CreateDefault() =>
        new(
            ThreatSeverity.Unknown,
            UnknownCount: null,
            NeutralCount: null,
            HostileCount: null,
            IsSafe: null,
            LastThreatChangedAtUtc: null,
            Signals: []);
}
