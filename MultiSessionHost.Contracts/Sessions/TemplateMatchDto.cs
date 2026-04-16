using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record TemplateMatchDto(
    string TemplateName,
    string TemplateKind,
    double Confidence,
    UiBounds Bounds,
    string SourceArtifactName,
    string? SourceRegionName,
    double? MatchScore,
    double Threshold,
    IReadOnlyDictionary<string, string?> Metadata);
