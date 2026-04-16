using System.Globalization;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;

namespace MultiSessionHost.Desktop.Regions;

internal static class ScreenRegionResolutionModels
{
    public static SessionScreenRegionResolution Create(
        SessionScreenSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        string regionLayoutProfile,
        string locatorSetName,
        ScreenRegionLocatorResult locatorResult) =>
        new(
            snapshot.SessionId,
            locatorResult.ResolvedAtUtc,
            snapshot.Sequence,
            snapshot.CapturedAtUtc,
            snapshot.TargetKind,
            snapshot.ObservabilityBackend,
            snapshot.CaptureBackend,
            context.Profile.ProfileName,
            regionLayoutProfile,
            locatorSetName,
            locatorResult.LocatorName,
            snapshot.ImageWidth,
            snapshot.ImageHeight,
            locatorResult.RegionSet.Regions.Count,
            locatorResult.RegionSet.Regions.Count(static region => region.MatchState == ScreenRegionMatchState.Matched),
            locatorResult.RegionSet.Regions.Count(static region => region.MatchState == ScreenRegionMatchState.Missing),
            locatorResult.RegionSet.Regions,
            locatorResult.Warnings,
            locatorResult.Errors,
            MergeMetadata(snapshot, context, locatorResult));

    public static SessionScreenRegionResolution CreateFailure(
        SessionScreenSnapshot? snapshot,
        ResolvedDesktopTargetContext context,
        string regionLayoutProfile,
        string locatorSetName,
        string locatorName,
        string errorMessage,
        DateTimeOffset resolvedAtUtc,
        IReadOnlyList<string>? warnings = null) =>
        new(
            context.SessionId,
            resolvedAtUtc,
            snapshot?.Sequence ?? 0,
            snapshot?.CapturedAtUtc ?? default,
            context.Target.Kind,
            snapshot?.ObservabilityBackend ?? GetMetadataValue(context.Target.Metadata, "ObservabilityBackend", "ScreenCapture"),
            snapshot?.CaptureBackend,
            context.Profile.ProfileName,
            regionLayoutProfile,
            locatorSetName,
            locatorName,
            snapshot?.ImageWidth ?? 0,
            snapshot?.ImageHeight ?? 0,
            1,
            0,
            1,
            [new ScreenRegionMatch(
                "window.full",
                "window",
                null,
                0d,
                locatorName,
                errorMessage,
                ScreenRegionMatchState.Missing,
                null,
                snapshot?.ImageWidth ?? 0,
                snapshot?.ImageHeight ?? 0,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["error"] = errorMessage,
                    ["regionLayoutProfile"] = regionLayoutProfile,
                    ["targetKind"] = context.Target.Kind.ToString()
                })],
            warnings is null ? [errorMessage] : warnings,
            [errorMessage],
            MergeMetadata(snapshot, context, null));

    private static IReadOnlyDictionary<string, string?> MergeMetadata(
        SessionScreenSnapshot? snapshot,
        ResolvedDesktopTargetContext context,
        ScreenRegionLocatorResult? locatorResult)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (snapshot is not null)
        {
            foreach (var (key, value) in snapshot.Metadata)
            {
                metadata[key] = value;
            }
        }

        foreach (var (key, value) in context.Target.Metadata)
        {
            metadata[key] = value;
        }

        metadata["sessionId"] = context.SessionId.Value;
        metadata["profileName"] = context.Profile.ProfileName;
        metadata["targetKind"] = context.Target.Kind.ToString();

        if (locatorResult is not null)
        {
            metadata["locatorName"] = locatorResult.LocatorName;
            metadata["regionLayoutProfile"] = locatorResult.RegionLayoutProfile;
            metadata["locatorSetName"] = locatorResult.RegionSet.LocatorSetName;
        }

        return metadata;
    }

    private static string GetMetadataValue(IReadOnlyDictionary<string, string?> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
}