using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class FramePreprocessingServiceTests
{
    [Fact]
    public async Task DefaultPreprocessing_ProducesDeterministicFullFrameArtifacts()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new MultiSessionHost.Core.Configuration.SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultFramePreprocessingService(snapshotStore, regionStore, preprocessingStore, clock, NullLogger<DefaultFramePreprocessingService>.Instance);
        var snapshot = CreateSnapshot(sessionId, sequence: 1, width: 4, height: 3, imageBytes: CreateTestPng(4, 3));
        await snapshotStore.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", metadata: null);

        var first = await service.PreprocessLatestAsync(sessionId, context, CancellationToken.None);
        var second = await service.PreprocessLatestAsync(sessionId, context, CancellationToken.None);
        var summary = await preprocessingStore.GetLatestSummaryAsync(sessionId, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(summary);
        Assert.Equal("DefaultFramePreprocessing", first!.PreprocessingProfileName);
        Assert.Contains(first.Artifacts, static artifact => artifact.ArtifactName == "frame.raw");
        var grayscaleFirst = Assert.Single(first.Artifacts, static artifact => artifact.ArtifactName == "frame.grayscale");
        var grayscaleSecond = Assert.Single(second!.Artifacts, static artifact => artifact.ArtifactName == "frame.grayscale");
        var contrastFirst = Assert.Single(first.Artifacts, static artifact => artifact.ArtifactName == "frame.high-contrast");
        var contrastSecond = Assert.Single(second.Artifacts, static artifact => artifact.ArtifactName == "frame.high-contrast");
        Assert.Equal(grayscaleFirst.ImageBytes, grayscaleSecond.ImageBytes);
        Assert.Equal(contrastFirst.ImageBytes, contrastSecond.ImageBytes);
        Assert.True(first.SuccessfulArtifactCount >= 3);
        Assert.Null(typeof(ProcessedFrameArtifactSummary).GetProperty("ImageBytes"));
    }

    [Fact]
    public async Task DefaultPreprocessing_ProducesRegionAwareCrops_WhenResolutionIsAvailable()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new MultiSessionHost.Core.Configuration.SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultFramePreprocessingService(snapshotStore, regionStore, preprocessingStore, clock, NullLogger<DefaultFramePreprocessingService>.Instance);
        var snapshot = CreateSnapshot(sessionId, sequence: 2, width: 12, height: 8, imageBytes: CreateTestPng(12, 8));
        await snapshotStore.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None);
        await regionStore.UpsertLatestAsync(sessionId, CreateResolution(snapshot), CancellationToken.None);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", metadata: null);

        var result = await service.PreprocessLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        var topRaw = Assert.Single(result!.Artifacts, static artifact => artifact.ArtifactName == "region:window.top.raw");
        var centerGray = Assert.Single(result.Artifacts, static artifact => artifact.ArtifactName == "region:window.center.grayscale");
        Assert.Equal("window.top", topRaw.SourceRegionName);
        Assert.Equal(12, topRaw.OutputWidth);
        Assert.Equal(2, topRaw.OutputHeight);
        Assert.Equal("window.center", centerGray.SourceRegionName);
        Assert.Equal(6, centerGray.OutputWidth);
        Assert.Equal(4, centerGray.OutputHeight);
    }

    [Fact]
    public async Task MissingRequestedRegions_AreRecordedWithoutBreakingFullFrameArtifacts()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new MultiSessionHost.Core.Configuration.SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultFramePreprocessingService(snapshotStore, regionStore, preprocessingStore, clock, NullLogger<DefaultFramePreprocessingService>.Instance);
        var snapshot = CreateSnapshot(sessionId, sequence: 3, width: 10, height: 10, imageBytes: CreateTestPng(10, 10));
        await snapshotStore.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None);
        await regionStore.UpsertLatestAsync(
            sessionId,
            CreateResolution(
                snapshot,
                [new ScreenRegionMatch("window.top", "strip", new UiBounds(0, 0, 10, 2), 1d, "locator", "ok", ScreenRegionMatchState.Matched, "top-edge", 10, 10, new Dictionary<string, string?>(StringComparer.Ordinal))]),
            CancellationToken.None);

        var context = CreateContext(
            sessionId,
            DesktopTargetKind.ScreenCaptureDesktop,
            "screen-profile",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FramePreprocessingRegionSet"] = "window.top,window.center"
            });

        var result = await service.PreprocessLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.FailedArtifactCount > 0);
        Assert.Contains(result.Artifacts, static artifact => artifact.ArtifactName == "frame.raw" && artifact.Errors.Count == 0);
        Assert.Contains(result.Artifacts, static artifact => artifact.ArtifactName == "region:window.center.raw" && artifact.Errors.Count > 0);
    }

    [Fact]
    public async Task InMemoryStore_UpsertLatest_ReplacesPreviousResultPerSession()
    {
        var sessionId = new SessionId("alpha");
        var store = new InMemorySessionFramePreprocessingStore();
        var first = CreateResult(sessionId, sequence: 1, processedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        var second = CreateResult(sessionId, sequence: 2, processedAtUtc: DateTimeOffset.UtcNow);

        await store.UpsertLatestAsync(sessionId, first, CancellationToken.None);
        await store.UpsertLatestAsync(sessionId, second, CancellationToken.None);

        var latest = await store.GetLatestAsync(sessionId, CancellationToken.None);
        var summary = await store.GetLatestSummaryAsync(sessionId, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.NotNull(summary);
        Assert.Equal(2, latest!.SourceSnapshotSequence);
        Assert.Equal(2, summary!.SourceSnapshotSequence);
    }

    [Fact]
    public async Task PreprocessLatestAsync_DoesNotPopulateStoreForUiaTargets()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new MultiSessionHost.Core.Configuration.SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultFramePreprocessingService(snapshotStore, regionStore, preprocessingStore, clock, NullLogger<DefaultFramePreprocessingService>.Instance);
        var context = CreateContext(sessionId, DesktopTargetKind.WindowsUiAutomationDesktop, "uia-profile", metadata: null);

        var result = await service.PreprocessLatestAsync(sessionId, context, CancellationToken.None);
        var stored = await preprocessingStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(stored);
    }

    private static SessionScreenSnapshot CreateSnapshot(SessionId sessionId, long sequence, int width, int height, byte[] imageBytes) =>
        new(
            sessionId,
            sequence,
            DateTimeOffset.UtcNow,
            ProcessId: 321,
            ProcessName: "ScreenApp",
            WindowHandle: 999,
            WindowTitle: "Screen Fixture",
            WindowBounds: new UiBounds(0, 0, width, height),
            ImageWidth: width,
            ImageHeight: height,
            ImageFormat: "image/png",
            PixelFormat: "Format32bppArgb",
            ImageBytes: imageBytes,
            PayloadByteLength: imageBytes.Length,
            TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
            CaptureSource: "ScreenCapture",
            ObservabilityBackend: "ScreenCapture",
            CaptureBackend: "FakeCapture",
            CaptureDurationMs: 1.25d,
            CaptureOrigin: "LiveRefresh",
            Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ObservabilityBackend"] = "ScreenCapture"
            });

    private static SessionFramePreprocessingResult CreateResult(SessionId sessionId, long sequence, DateTimeOffset processedAtUtc)
    {
        var artifact = new ProcessedFrameArtifact(
            "frame.raw",
            "raw",
            sequence,
            null,
            10,
            10,
            "image/png",
            3,
            ["passthrough"],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal),
            [1, 2, 3]);

        return new SessionFramePreprocessingResult(
            sessionId,
            processedAtUtc,
            sequence,
            processedAtUtc,
            sequence,
            processedAtUtc,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultFramePreprocessing",
            1,
            1,
            0,
            [artifact],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    private static SessionScreenRegionResolution CreateResolution(SessionScreenSnapshot snapshot, IReadOnlyList<ScreenRegionMatch>? overrideRegions = null)
    {
        var regions = overrideRegions ??
        [
            new ScreenRegionMatch("window.top", "strip", new UiBounds(0, 0, snapshot.ImageWidth, 2), 1d, "locator", "ok", ScreenRegionMatchState.Matched, "top-edge", snapshot.ImageWidth, snapshot.ImageHeight, new Dictionary<string, string?>(StringComparer.Ordinal)),
            new ScreenRegionMatch("window.center", "viewport", new UiBounds(3, 2, 6, 4), 0.9d, "locator", "ok", ScreenRegionMatchState.Inferred, "center", snapshot.ImageWidth, snapshot.ImageHeight, new Dictionary<string, string?>(StringComparer.Ordinal)),
            new ScreenRegionMatch("window.left", "panel", new UiBounds(0, 2, 3, 4), 0.9d, "locator", "ok", ScreenRegionMatchState.Matched, "left-edge", snapshot.ImageWidth, snapshot.ImageHeight, new Dictionary<string, string?>(StringComparer.Ordinal)),
            new ScreenRegionMatch("window.right", "panel", new UiBounds(9, 2, 3, 4), 0.9d, "locator", "ok", ScreenRegionMatchState.Matched, "right-edge", snapshot.ImageWidth, snapshot.ImageHeight, new Dictionary<string, string?>(StringComparer.Ordinal))
        ];

        return new SessionScreenRegionResolution(
            snapshot.SessionId,
            DateTimeOffset.UtcNow,
            snapshot.Sequence,
            snapshot.CapturedAtUtc,
            snapshot.TargetKind,
            snapshot.ObservabilityBackend,
            snapshot.CaptureBackend,
            "screen-profile",
            "DefaultDesktopGrid",
            "DefaultScreenRegionResolutionService",
            "DefaultDesktopGridRegionLocator",
            snapshot.ImageWidth,
            snapshot.ImageHeight,
            regions.Count,
            regions.Count(static region => region.MatchState != ScreenRegionMatchState.Missing),
            regions.Count(static region => region.MatchState == ScreenRegionMatchState.Missing),
            regions,
            [],
            [],
            new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    private static ResolvedDesktopTargetContext CreateContext(
        SessionId sessionId,
        DesktopTargetKind kind,
        string profileName,
        IReadOnlyDictionary<string, string?>? metadata)
    {
        var normalizedMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ObservabilityBackend"] = "ScreenCapture",
            ["RegionLayoutProfile"] = "DefaultDesktopGrid"
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                normalizedMetadata[key] = value;
            }
        }

        var profile = new DesktopTargetProfile(
            profileName,
            kind,
            "ScreenApp",
            null,
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            normalizedMetadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: false);

        var target = new DesktopSessionTarget(
            sessionId,
            profileName,
            kind,
            DesktopSessionMatchingMode.WindowTitle,
            "ScreenApp",
            null,
            null,
            null,
            normalizedMetadata);

        return new ResolvedDesktopTargetContext(
            sessionId,
            profile,
            new SessionTargetBinding(sessionId, profileName, new Dictionary<string, string>(StringComparer.Ordinal), Overrides: null),
            target,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static byte[] CreateTestPng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        255,
                        (x * 40 + y * 10) % 255,
                        (x * 20 + y * 30) % 255,
                        (x * 70 + y * 5) % 255));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
