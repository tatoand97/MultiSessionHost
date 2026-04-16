using System.Globalization;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Commands;

public sealed record ScreenTravelCommandMetadata(
    TravelAutopilotActionIntent TravelIntent,
    string ActionKind,
    string EvidenceSource,
    string? RegionName,
    string? ArtifactName,
    string? CandidateLabel,
    UiBounds RelativeBounds,
    long SourceSnapshotSequence,
    double Confidence,
    string Explanation,
    IReadOnlyDictionary<string, string> Diagnostics)
{
    public static bool TryParse(IReadOnlyDictionary<string, string?> metadata, out ScreenTravelCommandMetadata? parsed, out string? reason)
    {
        parsed = null;
        reason = null;

        if (!TryGet(metadata, "screenTravelIntent", out var intentValue) ||
            !Enum.TryParse<TravelAutopilotActionIntent>(intentValue, ignoreCase: true, out var travelIntent))
        {
            reason = "Missing or invalid screenTravelIntent metadata.";
            return false;
        }

        if (!TryGet(metadata, "screenActionKind", out var actionKind) ||
            !TryGet(metadata, "screenEvidenceSource", out var evidenceSource) ||
            !TryGet(metadata, "screenRelativeBounds", out var relativeBoundsValue) ||
            !TryGet(metadata, "screenSourceSnapshotSequence", out var sequenceValue) ||
            !TryGet(metadata, "screenSelectionConfidence", out var confidenceValue))
        {
            reason = "Screen travel metadata is incomplete.";
            return false;
        }

        if (!TryParseBounds(relativeBoundsValue, out var relativeBounds))
        {
            reason = "screenRelativeBounds is invalid.";
            return false;
        }

        if (!long.TryParse(sequenceValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceSnapshotSequence))
        {
            reason = "screenSourceSnapshotSequence is invalid.";
            return false;
        }

        if (!double.TryParse(confidenceValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
        {
            reason = "screenSelectionConfidence is invalid.";
            return false;
        }

        var diagnostics = metadata
            .Where(pair => pair.Key.StartsWith("screenDiagnostic.", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key[17..], static pair => pair.Value!, StringComparer.Ordinal);

        metadata.TryGetValue("screenRegionName", out var regionName);
        metadata.TryGetValue("screenArtifactName", out var artifactName);
        metadata.TryGetValue("screenCandidateLabel", out var candidateLabel);
        metadata.TryGetValue("screenExplanation", out var explanation);

        parsed = new ScreenTravelCommandMetadata(
            travelIntent,
            actionKind,
            evidenceSource,
            string.IsNullOrWhiteSpace(regionName) ? null : regionName,
            string.IsNullOrWhiteSpace(artifactName) ? null : artifactName,
            string.IsNullOrWhiteSpace(candidateLabel) ? null : candidateLabel,
            relativeBounds,
            sourceSnapshotSequence,
            confidence,
            explanation ?? string.Empty,
            diagnostics);
        return true;
    }

    private static bool TryParseBounds(string value, out UiBounds bounds)
    {
        var segments = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 4 ||
            !int.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(segments[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            bounds = default;
            return false;
        }

        bounds = new UiBounds(x, y, width, height);
        return true;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string?> metadata, string key, out string value)
    {
        if (metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
