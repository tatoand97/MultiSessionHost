using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class OcrExtractionServiceTests
{
    [Fact]
    public async Task ExtractLatestAsync_ConsumesPreprocessedArtifacts_InsteadOfRawSnapshot()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var engine = new RecordingOcrEngine();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultOcrExtractionService(preprocessingStore, ocrStore, engine, clock, NullLogger<DefaultOcrExtractionService>.Instance);
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

        var result = await service.ExtractLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Artifacts);
        Assert.Equal("region:window.top.threshold", result.Artifacts[0].ArtifactName);
        Assert.Single(engine.SeenArtifactNames);
        Assert.Equal("region:window.top.threshold", engine.SeenArtifactNames[0]);
        Assert.Single(engine.SeenPayloads);
        Assert.Equal(new byte[] { 7, 8, 9 }, engine.SeenPayloads[0]);
    }

    [Fact]
    public async Task ExtractLatestAsync_PrefersThresholdAndRegionArtifacts_WhenConfigured()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var engine = new RecordingOcrEngine();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultOcrExtractionService(preprocessingStore, ocrStore, engine, clock, NullLogger<DefaultOcrExtractionService>.Instance);
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
                ["OcrRegionSet"] = "window.top",
                ["OcrPreferredArtifactKinds"] = "threshold,high-contrast,grayscale,raw",
                ["OcrIncludeFullFrameFallback"] = bool.TrueString
            });

        var result = await service.ExtractLatestAsync(sessionId, context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Artifacts);
        var artifact = result.Artifacts[0];
        Assert.Equal("region:window.top.threshold", artifact.ArtifactName);
        Assert.Equal("threshold", artifact.SourceArtifactKind);
        Assert.False(artifact.UsedFullFrameFallback);
    }

    [Fact]
    public async Task ExtractLatestAsync_RecordsFailure_WhenPreprocessingIsMissing()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var engine = new RecordingOcrEngine();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultOcrExtractionService(preprocessingStore, ocrStore, engine, clock, NullLogger<DefaultOcrExtractionService>.Instance);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, metadata: null);

        var result = await service.ExtractLatestAsync(sessionId, context, CancellationToken.None);
        var stored = await ocrStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(stored);
        Assert.Equal(0, result!.TotalArtifactCount);
        Assert.Contains(result.Errors, static error => error.Contains("preprocessing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result, stored);
    }

    [Fact]
    public async Task InMemoryStore_UpsertLatest_ReplacesPreviousResultPerSession()
    {
        var sessionId = new SessionId("alpha");
        var store = new InMemorySessionOcrExtractionStore();
        var first = CreateOcrResult(sessionId, sourceSequence: 1, extractedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        var second = CreateOcrResult(sessionId, sourceSequence: 2, extractedAtUtc: DateTimeOffset.UtcNow);

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
    public async Task ExtractLatestAsync_DoesNotPopulateStoreForUiaTargets()
    {
        var sessionId = new SessionId("alpha");
        var preprocessingStore = new InMemorySessionFramePreprocessingStore();
        var ocrStore = new InMemorySessionOcrExtractionStore();
        var engine = new RecordingOcrEngine();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new DefaultOcrExtractionService(preprocessingStore, ocrStore, engine, clock, NullLogger<DefaultOcrExtractionService>.Instance);
        var context = CreateContext(sessionId, DesktopTargetKind.WindowsUiAutomationDesktop, metadata: null);

        var result = await service.ExtractLatestAsync(sessionId, context, CancellationToken.None);
        var stored = await ocrStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(stored);
        Assert.Empty(engine.SeenArtifactNames);
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

    private static SessionOcrExtractionResult CreateOcrResult(SessionId sessionId, long sourceSequence, DateTimeOffset extractedAtUtc)
    {
        return new SessionOcrExtractionResult(
            sessionId,
            extractedAtUtc,
            sourceSequence,
            extractedAtUtc,
            sourceSequence,
            extractedAtUtc,
            extractedAtUtc,
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenCapture",
            "FakeCapture",
            "DefaultRegionOcr",
            "FakeOcr",
            "FakeBackend",
            1,
            1,
            0,
            [
                new OcrArtifactResult(
                    "region:window.top.threshold",
                    "window.top",
                    "threshold",
                    ["crop", "threshold"],
                    "HELLO",
                    "HELLO",
                    0.95d,
                    1,
                    1,
                    "DefaultRegionAwareOcrSelectionV1",
                    false,
                    [new OcrTextFragment("HELLO", "HELLO", 0.95d, null, "region:window.top.threshold", "window.top")],
                    [new OcrTextLine("HELLO", "HELLO", 0.95d, null, "region:window.top.threshold", "window.top")],
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

    private sealed class RecordingOcrEngine : IOcrEngine
    {
        public List<string> SeenArtifactNames { get; } = [];

        public List<byte[]> SeenPayloads { get; } = [];

        public string EngineName => "RecordingOcr";

        public string BackendName => "TestBackend";

        public ValueTask<OcrEngineResult> ExtractAsync(ProcessedFrameArtifact artifact, CancellationToken cancellationToken)
        {
            SeenArtifactNames.Add(artifact.ArtifactName);
            SeenPayloads.Add(artifact.ImageBytes.ToArray());

            return ValueTask.FromResult(new OcrEngineResult(
                [new OcrEngineTextFragment($"TXT:{artifact.ArtifactName}", null, 0.8d, null)],
                [new OcrEngineTextLine($"TXT:{artifact.ArtifactName}", null, 0.8d, null)],
                0.8d,
                [],
                [],
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["engine"] = EngineName
                }));
        }

        public ValueTask<OcrEngineResult> ExtractAsync(
            byte[] imageBytes,
            string imageFormat,
            IReadOnlyDictionary<string, string?> metadata,
            CancellationToken cancellationToken)
        {
            SeenPayloads.Add(imageBytes.ToArray());

            return ValueTask.FromResult(new OcrEngineResult([], [], null, [], [], metadata));
        }
    }
}
