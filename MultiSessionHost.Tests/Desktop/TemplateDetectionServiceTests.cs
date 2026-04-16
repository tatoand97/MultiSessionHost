using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class TemplateDetectionServiceTests
{
    [Fact]
    public async Task DetectLatestAsync_ConsumesPreprocessedArtifacts_InsteadOfRawSnapshot()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var detectionStore = new InMemorySessionTemplateDetectionStore();
        var matcher = new RecordingMatcher();
        var registry = new StaticRegistry(CreateSingleTemplateSet("DefaultGenericMarkers"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultTemplateDetectionService(
            preprocessingStore,
            ocrStore,
            detectionStore,
            registry,
            matcher,
            clock,
            NullLogger<DefaultTemplateDetectionService>.Instance);

        var preprocessing = CreatePreprocessingResult(
            sessionId,
            sequence: 10,
            artifacts:
            [
                CreateArtifact("region:window.top.threshold", "threshold", "window.top", [7, 8, 9]),
                CreateArtifact("frame.raw", "raw", null, [1, 2, 3])
            ]);

        await preprocessingStore.UpsertLatestAsync(sessionId, preprocessing, CancellationToken.None);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, metadata: null);

        var result = await service.DetectLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Artifacts);
        Assert.Equal("region:window.top.threshold", result.Artifacts[0].ArtifactName);
        Assert.Single(matcher.SeenArtifactNames);
        Assert.Equal("region:window.top.threshold", matcher.SeenArtifactNames[0]);
        Assert.Single(matcher.SeenPayloads);
        Assert.Equal(new byte[] { 7, 8, 9 }, matcher.SeenPayloads[0]);
    }

    [Fact]
    public async Task DetectLatestAsync_PrefersThresholdAndRegionArtifacts_WhenConfigured()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var detectionStore = new InMemorySessionTemplateDetectionStore();
        var matcher = new RecordingMatcher();
        var registry = new StaticRegistry(CreateSingleTemplateSet("DefaultGenericMarkers"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultTemplateDetectionService(
            preprocessingStore,
            ocrStore,
            detectionStore,
            registry,
            matcher,
            clock,
            NullLogger<DefaultTemplateDetectionService>.Instance);

        var preprocessing = CreatePreprocessingResult(
            sessionId,
            sequence: 11,
            artifacts:
            [
                CreateArtifact("frame.threshold", "threshold", null, [1]),
                CreateArtifact("region:window.top.raw", "raw", "window.top", [2]),
                CreateArtifact("region:window.top.high-contrast", "high-contrast", "window.top", [3]),
                CreateArtifact("region:window.top.threshold", "threshold", "window.top", [4])
            ]);

        await preprocessingStore.UpsertLatestAsync(sessionId, preprocessing, CancellationToken.None);
        var context = CreateContext(
            sessionId,
            DesktopTargetKind.ScreenCaptureDesktop,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [DesktopTargetMetadata.TemplateRegionSet] = "window.top",
                [DesktopTargetMetadata.TemplatePreferredArtifactKinds] = "threshold,high-contrast,grayscale,raw",
                [DesktopTargetMetadata.TemplateIncludeFullFrameFallback] = bool.TrueString
            });

        var result = await service.DetectLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Artifacts);
        var artifact = result.Artifacts[0];
        Assert.Equal("region:window.top.threshold", artifact.ArtifactName);
        Assert.Equal("threshold", artifact.SourceArtifactKind);
        Assert.False(artifact.UsedFullFrameFallback);
    }

    [Fact]
    public void Registry_ResolvesTemplateSetsDeterministically()
    {
        var registry = new DefaultVisualTemplateRegistry();
        var sessionId = new SessionId("alpha");
        var baseContext = CreateContext(
            sessionId,
            DesktopTargetKind.ScreenCaptureDesktop,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [DesktopTargetMetadata.TemplateSet] = "DefaultGenericMarkers"
            });

        var profile = TemplateDetectionProfile.DefaultRegionTemplateDetection;
        var first = registry.Resolve(baseContext, profile);
        var second = registry.Resolve(baseContext, profile);

        Assert.Equal("DefaultGenericMarkers", first.TemplateSetName);
        Assert.True(first.Templates.Count >= 1);
        Assert.Equal(first.Templates.Select(static item => item.TemplateName), second.Templates.Select(static item => item.TemplateName));

        var unknownContext = CreateContext(
            sessionId,
            DesktopTargetKind.ScreenCaptureDesktop,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [DesktopTargetMetadata.TemplateSet] = "UnknownSet"
            });
        var unknown = registry.Resolve(unknownContext, profile);
        Assert.Empty(unknown.Templates);
    }

    [Fact]
    public async Task Matcher_ProducesDeterministicBoundsAndConfidence_OnSyntheticFixtures()
    {
        var matcher = new DeterministicTemplateMatcher(NullLogger<DeterministicTemplateMatcher>.Instance);
        var artifact = CreateArtifact(
            "region:window.top.threshold",
            "threshold",
            "window.top",
            CreateImageWithCrossAt(8, 8, 2, 4));

        var template = new VisualTemplateDefinition(
            "marker.cross",
            "marker",
            "DefaultGenericMarkers",
            ["threshold"],
            ["window.top"],
            0.98d,
            "image/png",
            CreateCrossTemplatePng(),
            ProviderReference: "test:marker.cross",
            Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        var result = await matcher.MatchAsync(artifact, [template], CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Single(result.Matches);
        var match = result.Matches[0];
        Assert.Equal(2, match.Bounds.X);
        Assert.Equal(4, match.Bounds.Y);
        Assert.Equal(3, match.Bounds.Width);
        Assert.Equal(3, match.Bounds.Height);
        Assert.True(match.Confidence >= 0.98d);
    }

    [Fact]
    public async Task InMemoryStore_UpsertLatest_ReplacesPreviousResultPerSession()
    {
        var sessionId = new SessionId("alpha");
        var store = new InMemorySessionTemplateDetectionStore();
        var first = CreateTemplateResult(sessionId, sourceSequence: 1, detectedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        var second = CreateTemplateResult(sessionId, sourceSequence: 2, detectedAtUtc: DateTimeOffset.UtcNow);

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
    public async Task DetectLatestAsync_RecordsFailure_WhenPreprocessingIsMissing()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var detectionStore = new InMemorySessionTemplateDetectionStore();
        var matcher = new RecordingMatcher();
        var registry = new StaticRegistry(CreateSingleTemplateSet("DefaultGenericMarkers"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultTemplateDetectionService(
            preprocessingStore,
            ocrStore,
            detectionStore,
            registry,
            matcher,
            clock,
            NullLogger<DefaultTemplateDetectionService>.Instance);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, metadata: null);

        var result = await service.DetectLatestAsync(sessionId, context, CancellationToken.None);
        var stored = await detectionStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(stored);
        Assert.Equal(0, result!.TotalArtifactCount);
        Assert.Contains(result.Errors, static error => error.Contains("preprocessing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result, stored);
    }

    [Fact]
    public async Task DetectLatestAsync_DoesNotPopulateStoreForUiaTargets()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var detectionStore = new InMemorySessionTemplateDetectionStore();
        var matcher = new RecordingMatcher();
        var registry = new StaticRegistry(CreateSingleTemplateSet("DefaultGenericMarkers"));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultTemplateDetectionService(
            preprocessingStore,
            ocrStore,
            detectionStore,
            registry,
            matcher,
            clock,
            NullLogger<DefaultTemplateDetectionService>.Instance);
        var context = CreateContext(sessionId, DesktopTargetKind.WindowsUiAutomationDesktop, metadata: null);

        var result = await service.DetectLatestAsync(sessionId, context, CancellationToken.None);
        var stored = await detectionStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(stored);
        Assert.Empty(matcher.SeenArtifactNames);
    }

    private static ProcessedFrameArtifact CreateArtifact(string name, string kind, string? regionName, byte[] bytes) =>
        new(
            name,
            kind,
            SourceSnapshotSequence: 1,
            SourceRegionName: regionName,
            OutputWidth: 10,
            OutputHeight: 4,
            ImageFormat: "image/png",
            PayloadByteLength: bytes.Length,
            PreprocessingSteps: ["step"],
            Warnings: [],
            Errors: [],
            Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            ImageBytes: bytes);

    private static SessionFramePreprocessingResult CreatePreprocessingResult(SessionId sessionId, long sequence, IReadOnlyList<ProcessedFrameArtifact> artifacts)
    {
        return new SessionFramePreprocessingResult(
            sessionId,
            DateTimeOffset.UtcNow,
            sequence,
            DateTimeOffset.UtcNow,
            sequence,
            DateTimeOffset.UtcNow,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultFramePreprocessing",
            artifacts.Count,
            artifacts.Count,
            0,
            artifacts,
            [],
            [],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
    }

    private static VisualTemplateSet CreateSingleTemplateSet(string setName) =>
        new(
            setName,
            "DefaultRegionTemplateDetection",
            [
                new VisualTemplateDefinition(
                    "marker.cross",
                    "marker",
                    setName,
                    ["threshold", "high-contrast", "grayscale", "raw"],
                    ["window.top", "window.center"],
                    0.7d,
                    "image/png",
                    CreateCrossTemplatePng(),
                    ProviderReference: "test:marker.cross",
                    Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
            ],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    private static SessionTemplateDetectionResult CreateTemplateResult(SessionId sessionId, long sourceSequence, DateTimeOffset detectedAtUtc)
    {
        return new SessionTemplateDetectionResult(
            sessionId,
            detectedAtUtc,
            sourceSequence,
            detectedAtUtc,
            sourceSequence,
            detectedAtUtc,
            detectedAtUtc,
            detectedAtUtc,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultRegionTemplateDetection",
            "DefaultGenericMarkers",
            "FakeMatcher",
            "FakeBackend",
            1,
            1,
            1,
            0,
            [
                new TemplateArtifactResult(
                    "region:window.top.threshold",
                    "window.top",
                    "threshold",
                    "DefaultRegionAwareTemplateSelectionV1",
                    false,
                    1,
                    1,
                    [
                        new TemplateMatch(
                            "marker.cross",
                            "marker",
                            0.95d,
                            new UiBounds(2, 1, 3, 3),
                            "region:window.top.threshold",
                            "window.top",
                            0.95d,
                            0.9d,
                            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
                    ],
                    [],
                    [],
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
            ],
            [],
            [],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));
    }

    private static ResolvedDesktopTargetContext CreateContext(
        SessionId sessionId,
        DesktopTargetKind kind,
        IReadOnlyDictionary<string, string?>? metadata)
    {
        var normalizedMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [DesktopTargetMetadata.ObservabilityBackend] = "ScreenCapture",
            [DesktopTargetMetadata.RegionLayoutProfile] = "DefaultDesktopGrid"
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                normalizedMetadata[key] = value;
            }
        }

        var profile = new DesktopTargetProfile(
            "screen-profile",
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
            "screen-profile",
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
            new SessionTargetBinding(sessionId, "screen-profile", new Dictionary<string, string>(StringComparer.Ordinal), Overrides: null),
            target,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static byte[] CreateCrossTemplatePng()
    {
        using var bitmap = new Bitmap(3, 3, PixelFormat.Format32bppArgb);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, Color.Black);
            }
        }

        bitmap.SetPixel(1, 0, Color.White);
        bitmap.SetPixel(0, 1, Color.White);
        bitmap.SetPixel(1, 1, Color.White);
        bitmap.SetPixel(2, 1, Color.White);
        bitmap.SetPixel(1, 2, Color.White);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] CreateImageWithCrossAt(int width, int height, int xStart, int yStart)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.Black);
            }
        }

        bitmap.SetPixel(xStart + 1, yStart + 0, Color.White);
        bitmap.SetPixel(xStart + 0, yStart + 1, Color.White);
        bitmap.SetPixel(xStart + 1, yStart + 1, Color.White);
        bitmap.SetPixel(xStart + 2, yStart + 1, Color.White);
        bitmap.SetPixel(xStart + 1, yStart + 2, Color.White);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class StaticRegistry : IVisualTemplateRegistry
    {
        private readonly VisualTemplateSet _set;

        public StaticRegistry(VisualTemplateSet set)
        {
            _set = set;
        }

        public VisualTemplateSet Resolve(ResolvedDesktopTargetContext context, TemplateDetectionProfile profile) => _set;
    }

    private sealed class RecordingMatcher : ITemplateMatcher
    {
        public List<string> SeenArtifactNames { get; } = [];

        public List<byte[]> SeenPayloads { get; } = [];

        public string MatcherName => "RecordingMatcher";

        public string BackendName => "TestBackend";

        public ValueTask<TemplateMatcherArtifactResult> MatchAsync(ProcessedFrameArtifact artifact, IReadOnlyList<VisualTemplateDefinition> templates, CancellationToken cancellationToken)
        {
            SeenArtifactNames.Add(artifact.ArtifactName);
            SeenPayloads.Add(artifact.ImageBytes.ToArray());

            var first = templates.FirstOrDefault();
            var matches = first is null
                ? Array.Empty<TemplateMatcherMatch>()
                :
                [
                    new TemplateMatcherMatch(
                        first.TemplateName,
                        first.TemplateKind,
                        0.8d,
                        new UiBounds(0, 0, 1, 1),
                        0.8d,
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
                ];

            return ValueTask.FromResult(new TemplateMatcherArtifactResult(matches, [], [], new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)));
        }
    }
}