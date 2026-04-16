namespace MultiSessionHost.Contracts.Sessions;

public sealed record TemplateArtifactResultSummaryDto(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    int EvaluatedTemplateCount,
    int MatchedTemplateCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
