namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionScreenRegionResolutionDto(
    string SessionId,
    DateTimeOffset ResolvedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    string TargetKind,
    string ObservabilityBackend,
    string? CaptureBackend,
    string? TargetProfileName,
    string RegionLayoutProfile,
    string LocatorSetName,
    string LocatorName,
    int TargetImageWidth,
    int TargetImageHeight,
    int TotalRegionsRequested,
    int MatchedRegionCount,
    int MissingRegionCount,
    IReadOnlyList<ScreenRegionMatchDto> Regions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);