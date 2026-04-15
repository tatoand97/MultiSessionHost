using System.Globalization;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Extraction;

namespace MultiSessionHost.Desktop.Risk;

public sealed class DefaultRiskCandidateBuilder : IRiskCandidateBuilder
{
    public IReadOnlyList<RiskCandidate> BuildCandidates(UiSemanticExtractionResult semanticExtraction)
    {
        ArgumentNullException.ThrowIfNull(semanticExtraction);

        var candidates = new List<RiskCandidate>();

        candidates.AddRange(semanticExtraction.Targets.Select(target => FromTarget(semanticExtraction, target)));
        candidates.AddRange(semanticExtraction.PresenceEntities.Select(entity => FromPresence(semanticExtraction, entity)));
        candidates.AddRange(semanticExtraction.Alerts.Where(static alert => alert.Visible).Select(alert => FromAlert(semanticExtraction, alert)));
        candidates.AddRange(semanticExtraction.TransitStates.Select((state, index) => FromTransit(semanticExtraction, state, index)));
        candidates.AddRange(semanticExtraction.Resources.Select(resource => FromResource(semanticExtraction, resource)));
        candidates.AddRange(semanticExtraction.Capabilities.Select(capability => FromCapability(semanticExtraction, capability)));

        return candidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Name))
            .ToArray();
    }

    private static RiskCandidate FromTarget(UiSemanticExtractionResult result, DetectedTarget target)
    {
        var tags = new List<string> { "target", target.Kind.ToString() };

        if (target.Selected)
        {
            tags.Add("selected");
        }

        if (target.Active)
        {
            tags.Add("active");
        }

        if (target.Focused)
        {
            tags.Add("focused");
        }

        return new RiskCandidate(
            $"target:{target.NodeId}",
            result.SessionId,
            RiskEntitySource.Target,
            CleanName(target.Label) ?? target.NodeId,
            target.Kind.ToString(),
            Normalize(tags),
            BuildSignals(target.Confidence, target.Kind.ToString()),
            ToScore(target.Confidence),
            BuildMetadata(
                ("nodeId", target.NodeId),
                ("count", target.Count?.ToString(CultureInfo.InvariantCulture)),
                ("index", target.Index?.ToString(CultureInfo.InvariantCulture))));
    }

    private static RiskCandidate FromPresence(UiSemanticExtractionResult result, DetectedPresenceEntity entity)
    {
        var tags = new List<string> { "presence", entity.Kind.ToString() };
        tags.AddRange(entity.Membership);

        if (!string.IsNullOrWhiteSpace(entity.Status))
        {
            tags.Add(entity.Status);
        }

        if (entity.Count is null or <= 0)
        {
            tags.Add("unknown");
        }

        return new RiskCandidate(
            $"presence:{entity.NodeId}",
            result.SessionId,
            RiskEntitySource.Presence,
            CleanName(entity.Label) ?? entity.NodeId,
            entity.Kind.ToString(),
            Normalize(tags),
            BuildSignals(entity.Confidence, entity.Status),
            ToScore(entity.Confidence),
            BuildMetadata(
                ("nodeId", entity.NodeId),
                ("count", entity.Count?.ToString(CultureInfo.InvariantCulture))));
    }

    private static RiskCandidate FromAlert(UiSemanticExtractionResult result, DetectedAlert alert)
    {
        var tags = new List<string> { "alert", alert.Severity.ToString() };

        if (alert.Severity is AlertSeverity.Warning or AlertSeverity.Error or AlertSeverity.Critical)
        {
            tags.Add(alert.Severity.ToString().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(alert.SourceHint))
        {
            tags.Add(alert.SourceHint);
        }

        return new RiskCandidate(
            $"alert:{alert.NodeId}",
            result.SessionId,
            RiskEntitySource.Alert,
            alert.Message.Trim(),
            alert.Severity.ToString(),
            Normalize(tags),
            BuildSignals(alert.Confidence, alert.SourceHint),
            ToScore(alert.Confidence),
            BuildMetadata(
                ("nodeId", alert.NodeId),
                ("visible", alert.Visible.ToString(CultureInfo.InvariantCulture))));
    }

    private static RiskCandidate FromTransit(UiSemanticExtractionResult result, DetectedTransitState state, int index)
    {
        var tags = new List<string> { "transit", state.Status.ToString() };
        tags.AddRange(state.Reasons);

        return new RiskCandidate(
            $"transit:{index}:{string.Join("-", state.NodeIds)}",
            result.SessionId,
            RiskEntitySource.Transit,
            CleanName(state.Label) ?? state.Status.ToString(),
            state.Status.ToString(),
            Normalize(tags),
            state.Reasons,
            ToScore(state.Confidence),
            BuildMetadata(
                ("nodeIds", string.Join(",", state.NodeIds)),
                ("progressPercent", state.ProgressPercent?.ToString(CultureInfo.InvariantCulture))));
    }

    private static RiskCandidate FromResource(UiSemanticExtractionResult result, DetectedResource resource)
    {
        var tags = new List<string> { "resource", resource.Kind.ToString() };

        if (resource.Degraded)
        {
            tags.Add("degraded");
        }

        if (resource.Critical)
        {
            tags.Add("critical");
        }

        return new RiskCandidate(
            $"resource:{resource.NodeId}",
            result.SessionId,
            RiskEntitySource.Resource,
            CleanName(resource.Name) ?? resource.NodeId,
            resource.Kind.ToString(),
            Normalize(tags),
            BuildSignals(resource.Confidence, resource.Kind.ToString()),
            ToScore(resource.Confidence),
            BuildMetadata(
                ("nodeId", resource.NodeId),
                ("percent", resource.Percent?.ToString(CultureInfo.InvariantCulture)),
                ("value", resource.Value?.ToString(CultureInfo.InvariantCulture))));
    }

    private static RiskCandidate FromCapability(UiSemanticExtractionResult result, DetectedCapability capability)
    {
        var tags = new List<string> { "capability", capability.Status.ToString() };

        if (capability.Enabled)
        {
            tags.Add("enabled");
        }

        if (capability.Active)
        {
            tags.Add("active");
        }

        if (capability.CoolingDown)
        {
            tags.Add("cooling-down");
        }

        return new RiskCandidate(
            $"capability:{capability.NodeId}",
            result.SessionId,
            RiskEntitySource.Capability,
            CleanName(capability.Name) ?? capability.NodeId,
            capability.Status.ToString(),
            Normalize(tags),
            BuildSignals(capability.Confidence, capability.Status.ToString()),
            ToScore(capability.Confidence),
            BuildMetadata(("nodeId", capability.NodeId)));
    }

    private static IReadOnlyList<string> BuildSignals(DetectionConfidence confidence, string? signal) =>
        string.IsNullOrWhiteSpace(signal)
            ? [$"confidence:{confidence}"]
            : [$"confidence:{confidence}", signal.Trim()];

    private static IReadOnlyList<string> Normalize(IEnumerable<string?> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? CleanName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double ToScore(DetectionConfidence confidence) =>
        confidence switch
        {
            DetectionConfidence.Low => 0.35,
            DetectionConfidence.Medium => 0.65,
            DetectionConfidence.High => 0.9,
            _ => 0.0
        };

    private static IReadOnlyDictionary<string, string> BuildMetadata(params (string Key, string? Value)[] values) =>
        values
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
}
