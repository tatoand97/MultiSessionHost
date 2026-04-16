using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Templates;

public interface ITemplateMatcher
{
    string MatcherName { get; }

    string BackendName { get; }

    ValueTask<TemplateMatcherArtifactResult> MatchAsync(
        ProcessedFrameArtifact artifact,
        IReadOnlyList<VisualTemplateDefinition> templates,
        CancellationToken cancellationToken);
}

public sealed record TemplateMatcherArtifactResult(
    IReadOnlyList<TemplateMatcherMatch> Matches,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record TemplateMatcherMatch(
    string TemplateName,
    string TemplateKind,
    double Confidence,
    UiBounds Bounds,
    double RawScore,
    IReadOnlyDictionary<string, string?> Metadata);
