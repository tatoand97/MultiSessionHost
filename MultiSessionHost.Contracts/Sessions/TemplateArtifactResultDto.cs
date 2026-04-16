namespace MultiSessionHost.Contracts.Sessions;

public sealed record TemplateArtifactResultDto(
    string ArtifactName,
    string? SourceRegionName,
    string SourceArtifactKind,
    string SelectionStrategy,
    bool UsedFullFrameFallback,
    int EvaluatedTemplateCount,
    int MatchedTemplateCount,
    IReadOnlyList<TemplateMatchDto> Matches,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);
