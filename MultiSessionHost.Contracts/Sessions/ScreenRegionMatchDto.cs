using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record ScreenRegionMatchDto(
    string RegionName,
    string RegionKind,
    UiBounds? Bounds,
    double Confidence,
    string SourceLocatorName,
    string ResolutionReason,
    string MatchState,
    string? AnchorStrategy,
    int TargetImageWidth,
    int TargetImageHeight,
    IReadOnlyDictionary<string, string?> Metadata);