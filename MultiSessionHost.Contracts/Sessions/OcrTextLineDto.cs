using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record OcrTextLineDto(
    string Text,
    string NormalizedText,
    double? Confidence,
    UiBounds? Bounds,
    string SourceArtifactName,
    string? SourceRegionName);
