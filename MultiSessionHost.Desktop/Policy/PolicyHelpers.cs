using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

internal static class PolicyHelpers
{
    public static RiskEntityAssessment? GetTopThreat(RiskAssessmentResult? riskAssessment) =>
        riskAssessment?.Entities
            .Where(static entity => entity.Disposition == RiskDisposition.Threat)
            .OrderByDescending(static entity => entity.Priority)
            .ThenByDescending(static entity => entity.Severity)
            .ThenBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.CandidateId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static IReadOnlyDictionary<string, string> Metadata(params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    public static bool IsSevere(ThreatSeverity severity) =>
        severity is ThreatSeverity.High or ThreatSeverity.Critical;

    public static bool IsCritical(RiskSeverity severity) =>
        severity == RiskSeverity.Critical;
}
