using System.Globalization;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Regions;

public abstract class DesktopGridRegionLocatorBase : IScreenRegionLocator, IRegionLocatorPriority
{
    protected abstract double TopStripRatio { get; }

    protected abstract double BottomStripRatio { get; }

    protected abstract double SidePanelRatio { get; }

    protected abstract double SafeInsetRatio { get; }

    public abstract string LocatorName { get; }

    public abstract string RegionLayoutProfile { get; }

    public virtual int Priority => 0;

    public bool Supports(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile) =>
        context.Target.Kind == DesktopTargetKind.ScreenCaptureDesktop && string.Equals(regionLayoutProfile, RegionLayoutProfile, StringComparison.OrdinalIgnoreCase);

    public ValueTask<ScreenRegionLocatorResult> ResolveAsync(
        SessionScreenSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        string regionLayoutProfile,
        DateTimeOffset resolvedAtUtc,
        CancellationToken cancellationToken)
    {
        var width = snapshot.ImageWidth;
        var height = snapshot.ImageHeight;
        var topHeight = ClampSize((int)Math.Round(height * TopStripRatio, MidpointRounding.AwayFromZero), 24, Math.Max(24, height / 4));
        var bottomHeight = ClampSize((int)Math.Round(height * BottomStripRatio, MidpointRounding.AwayFromZero), 24, Math.Max(24, height / 4));
        var sideWidth = ClampSize((int)Math.Round(width * SidePanelRatio, MidpointRounding.AwayFromZero), 48, Math.Max(48, width / 3));
        var safeInset = ClampSize((int)Math.Round(Math.Min(width, height) * SafeInsetRatio, MidpointRounding.AwayFromZero), 8, Math.Max(8, Math.Min(width, height) / 5));

        var regions = new List<ScreenRegionMatch>(7)
        {
            CreateMatch("window.full", "window", new UiBounds(0, 0, width, height), 1.0d, "frame", "The region spans the full captured frame.", ScreenRegionMatchState.Matched, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.top", "strip", new UiBounds(0, 0, width, topHeight), 0.95d, "top-edge", "The top strip is derived from the layout profile.", ScreenRegionMatchState.Matched, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.left", "panel", new UiBounds(0, topHeight, sideWidth, Math.Max(0, height - topHeight - bottomHeight)), 0.9d, "left-edge", "The left panel is derived from the layout profile.", ScreenRegionMatchState.Matched, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.right", "panel", new UiBounds(Math.Max(0, width - sideWidth), topHeight, sideWidth, Math.Max(0, height - topHeight - bottomHeight)), 0.9d, "right-edge", "The right panel is derived from the layout profile.", ScreenRegionMatchState.Matched, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.bottom", "strip", new UiBounds(0, Math.Max(0, height - bottomHeight), width, bottomHeight), 0.95d, "bottom-edge", "The bottom strip is derived from the layout profile.", ScreenRegionMatchState.Matched, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.center", "viewport", new UiBounds(sideWidth, topHeight, Math.Max(0, width - (sideWidth * 2)), Math.Max(0, height - topHeight - bottomHeight)), 0.88d, "frame-center", "The center viewport is inferred from the surrounding strips and panels.", ScreenRegionMatchState.Inferred, width, height, LocatorName, regionLayoutProfile, snapshot, context),
            CreateMatch("window.safe", "safe-area", new UiBounds(safeInset, safeInset, Math.Max(0, width - (safeInset * 2)), Math.Max(0, height - (safeInset * 2))), 0.82d, "inset", "The safe area is inferred from a conservative inset.", ScreenRegionMatchState.Inferred, width, height, LocatorName, regionLayoutProfile, snapshot, context)
        };

        var result = new ScreenRegionLocatorResult(
            snapshot.SessionId,
            LocatorName,
            regionLayoutProfile,
            context.Target.Kind,
            GetMetadataValue(snapshot.Metadata, "observabilityBackend", "ScreenCapture"),
            context.Profile.ProfileName,
            snapshot.Sequence,
            snapshot.CapturedAtUtc,
            resolvedAtUtc,
            width,
            height,
            new ScreenRegionSet(regionLayoutProfile, LocatorName, regions),
            Array.Empty<string>(),
            Array.Empty<string>(),
            BuildMetadata(context, snapshot, regionLayoutProfile));

        return ValueTask.FromResult(result);
    }

    private static ScreenRegionMatch CreateMatch(
        string regionName,
        string regionKind,
        UiBounds? bounds,
        double confidence,
        string anchorStrategy,
        string reason,
        ScreenRegionMatchState state,
        int width,
        int height,
        string locatorName,
        string regionLayoutProfile,
        SessionScreenSnapshot snapshot,
        ResolvedDesktopTargetContext context)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["anchorStrategy"] = anchorStrategy,
            ["regionLayoutProfile"] = regionLayoutProfile,
            ["targetKind"] = context.Target.Kind.ToString(),
            ["profileName"] = context.Profile.ProfileName,
            ["targetImageWidth"] = width.ToString(CultureInfo.InvariantCulture),
            ["targetImageHeight"] = height.ToString(CultureInfo.InvariantCulture)
        };

        return new ScreenRegionMatch(
            regionName,
            regionKind,
            bounds,
            confidence,
            locatorName,
            reason,
            state,
            anchorStrategy,
            width,
            height,
            metadata);
    }

    private static IReadOnlyDictionary<string, string?> BuildMetadata(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["sessionId"] = context.SessionId.Value,
            ["profileName"] = context.Profile.ProfileName,
            ["targetKind"] = context.Target.Kind.ToString(),
            ["regionLayoutProfile"] = regionLayoutProfile,
            ["sourceSnapshotSequence"] = snapshot.Sequence.ToString(CultureInfo.InvariantCulture),
            ["sourceSnapshotCapturedAtUtc"] = snapshot.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };

    private static int ClampSize(int value, int minimum, int maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private static string GetMetadataValue(IReadOnlyDictionary<string, string?> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
}