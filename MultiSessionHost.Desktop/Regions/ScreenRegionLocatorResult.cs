using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Regions;

public sealed record ScreenRegionLocatorResult(
    SessionId SessionId,
    string LocatorName,
    string RegionLayoutProfile,
    DesktopTargetKind TargetKind,
    string ObservabilityBackend,
    string? TargetProfileName,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    DateTimeOffset ResolvedAtUtc,
    int TargetImageWidth,
    int TargetImageHeight,
    ScreenRegionSet RegionSet,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata);