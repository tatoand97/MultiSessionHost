using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Regions;

public sealed record SessionScreenRegionResolution(
    SessionId SessionId,
    DateTimeOffset ResolvedAtUtc,
    long SourceSnapshotSequence,
    DateTimeOffset SourceSnapshotCapturedAtUtc,
    DesktopTargetKind TargetKind,
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
    IReadOnlyList<ScreenRegionMatch> Regions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public SessionScreenRegionSummary ToSummary() =>
        new(
            SessionId,
            ResolvedAtUtc,
            SourceSnapshotSequence,
            SourceSnapshotCapturedAtUtc,
            TargetKind,
            ObservabilityBackend,
            CaptureBackend,
            TargetProfileName,
            RegionLayoutProfile,
            LocatorSetName,
            LocatorName,
            TargetImageWidth,
            TargetImageHeight,
            TotalRegionsRequested,
            MatchedRegionCount,
            MissingRegionCount,
            Warnings,
            Errors,
            Metadata);
}