using MultiSessionHost.AdminApi.Mapping;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class EveLikeScreenRouteSemanticPackageTests
{
    [Fact]
    public async Task ScreenBackedRoute_ExtractsTravelRoute_FromVisualEvidence()
    {
        var sessionId = new SessionId("screen-route-1");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.92d,
                    "Route",
                    "Destination: Jita",
                    "Current location: Perimeter",
                    "Next waypoint: New Caldari",
                    "Sobaseki",
                    "Sobaseki")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var route = result.EveLike!.TravelRoute;

        Assert.True(route.RouteActive);
        Assert.Equal("Jita", route.DestinationLabel);
        Assert.Equal("Perimeter", route.CurrentLocationLabel);
        Assert.Equal("New Caldari", route.NextWaypointLabel);
        Assert.Equal(1, route.WaypointCount);
        Assert.Equal(["Sobaseki"], route.VisibleWaypoints);
        Assert.Contains(route.Reasons, reason => reason.Contains("Route panel/header", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScreenBackedRoute_DoesNotActivate_FromWeakHeaderOnlyEvidence()
    {
        var sessionId = new SessionId("screen-route-2");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.90d,
                    "Route")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var route = result.EveLike!.TravelRoute;

        Assert.False(route.RouteActive);
        Assert.Equal(DetectionConfidence.Low, route.Confidence);
        Assert.Contains(route.Reasons, reason => reason.Contains("not asserted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScreenBackedRoute_DeduplicatesAndNormalizesWaypoints()
    {
        var sessionId = new SessionId("screen-route-3");
        var context = CreateScreenPackageContext(
            sessionId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["EveLike.RouteMinWaypointsForActive"] = "1"
            });
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.90d,
                    "Route",
                    "1. Jita",
                    "> jita",
                    "JITA",
                    "New Caldari",
                    "New   Caldari")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var route = result.EveLike!.TravelRoute;

        Assert.True(route.RouteActive);
        Assert.Equal(2, route.WaypointCount);
        Assert.Equal(["Jita", "New Caldari"], route.VisibleWaypoints);
    }

    [Fact]
    public async Task ScreenBackedRoute_TemplateSupport_CanRaiseConfidence()
    {
        var sessionId = new SessionId("screen-route-4");
        var context = CreateScreenPackageContext(sessionId, metadata: null);

        var withoutTemplateStores = CreateStores();
        await SeedVisualStoresAsync(
            sessionId,
            withoutTemplateStores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.89d,
                    "Route",
                    "Jita",
                    "Perimeter")
            ],
            templateMatches: []);

        var withoutTemplateResult = await CreatePackage(withoutTemplateStores).ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(withoutTemplateResult.EveLike);
        var withoutTemplateConfidence = withoutTemplateResult.EveLike!.TravelRoute.Confidence;

        var withTemplateStores = CreateStores();
        await SeedVisualStoresAsync(
            sessionId,
            withTemplateStores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.89d,
                    "Route",
                    "Jita",
                    "Perimeter")
            ],
            templateMatches:
            [
                new TemplateMatch(
                    "route.panel.marker",
                    "route",
                    0.95d,
                    new UiBounds(5, 5, 10, 10),
                    "region:window.right.threshold",
                    "window.right",
                    0.95d,
                    0.90d,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
            ]);

        var withTemplateResult = await CreatePackage(withTemplateStores).ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(withTemplateResult.EveLike);
        var withTemplateConfidence = withTemplateResult.EveLike!.TravelRoute.Confidence;

        Assert.Equal(DetectionConfidence.Medium, withoutTemplateConfidence);
        Assert.Equal(DetectionConfidence.High, withTemplateConfidence);
    }

    [Fact]
    public async Task ScreenBackedRoute_MissingOcr_DegradesGracefullyWithWarnings()
    {
        var sessionId = new SessionId("screen-route-5");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(sessionId, stores, ocrArtifacts: null, templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var route = result.EveLike!.TravelRoute;

        Assert.False(route.RouteActive);
        Assert.Equal(DetectionConfidence.Unknown, route.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Contains("OCR evidence was unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScreenBackedRoute_LeavesOtherSectionsConservative()
    {
        var sessionId = new SessionId("screen-route-6");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.91d,
                    "Route",
                    "Destination: Jita",
                    "Perimeter")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var eveLike = result.EveLike!;

        Assert.False(eveLike.Presence.IsVisible);
        Assert.Empty(eveLike.OverviewEntries);
        Assert.Empty(eveLike.ProbeScannerEntries);
        Assert.Equal(DetectionConfidence.Unknown, eveLike.Tactical.Confidence);
        Assert.Equal(DetectionConfidence.Unknown, eveLike.Safety.Confidence);
    }

    [Fact]
    public async Task ScreenBackedRoute_DoesNotFabricateRouteFields_WhenAbsent()
    {
        var sessionId = new SessionId("screen-route-7");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.87d,
                    "Route",
                    "Niarja",
                    "Tama")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        Assert.NotNull(result.EveLike);
        var route = result.EveLike!.TravelRoute;

        Assert.True(route.RouteActive);
        Assert.Null(route.DestinationLabel);
        Assert.Null(route.CurrentLocationLabel);
        Assert.Null(route.NextWaypointLabel);
    }

    [Fact]
    public async Task ScreenBackedRoute_IsVisibleThroughExistingSemanticDtoSurface()
    {
        var sessionId = new SessionId("screen-route-8");
        var context = CreateScreenPackageContext(sessionId, metadata: null);
        var stores = CreateStores();

        await SeedVisualStoresAsync(
            sessionId,
            stores,
            ocrArtifacts:
            [
                CreateOcrArtifact("region:window.right.threshold", "window.right", "threshold", 0.92d,
                    "Route",
                    "Destination: Jita",
                    "Sobaseki")
            ],
            templateMatches: []);

        var package = CreatePackage(stores);
        var result = await package.ExtractAsync(context, CancellationToken.None);
        var dto = result.EveLike!.ToDto();

        Assert.Equal("Jita", dto.TravelRoute.DestinationLabel);
        Assert.Equal(1, dto.TravelRoute.WaypointCount);
        Assert.Equal(["Sobaseki"], dto.TravelRoute.VisibleWaypoints);
    }

    private static EveLikeSemanticPackage CreatePackage(VisualStores stores) =>
        new(
            new DefaultUiTreeQueryService(),
            stores.SnapshotStore,
            stores.RegionStore,
            stores.PreprocessingStore,
            stores.OcrStore,
            stores.TemplateStore);

    private static VisualStores CreateStores() =>
        new(
            new InMemorySessionScreenSnapshotStore(new Core.Configuration.SessionHostOptions()),
            new InMemorySessionScreenRegionStore(),
            new InMemorySessionFramePreprocessingStore(),
            new InMemorySessionOcrExtractionStore(),
            new InMemorySessionTemplateDetectionStore());

    private static async Task SeedVisualStoresAsync(
        SessionId sessionId,
        VisualStores stores,
        IReadOnlyList<OcrArtifactResult>? ocrArtifacts,
        IReadOnlyList<TemplateMatch> templateMatches)
    {
        var now = DateTimeOffset.Parse("2026-04-16T10:00:00Z");

        await stores.SnapshotStore.UpsertLatestAsync(
            sessionId,
            new SessionScreenSnapshot(
                sessionId,
                Sequence: 10,
                now,
                ProcessId: 111,
                ProcessName: "ScreenApp",
                WindowHandle: 999,
                WindowTitle: "EVE",
                new UiBounds(0, 0, 1600, 900),
                ImageWidth: 1600,
                ImageHeight: 900,
                ImageFormat: "image/png",
                PixelFormat: "Format32bppArgb",
                ImageBytes: [1, 2, 3],
                PayloadByteLength: 3,
                TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
                CaptureSource: "ScreenCapture",
                ObservabilityBackend: "ScreenCapture",
                CaptureBackend: "FakeCapture",
                CaptureDurationMs: 5d,
                CaptureOrigin: "Test",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        await stores.RegionStore.UpsertLatestAsync(
            sessionId,
            new SessionScreenRegionResolution(
                sessionId,
                now,
                SourceSnapshotSequence: 10,
                SourceSnapshotCapturedAtUtc: now,
                TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
                ObservabilityBackend: "ScreenCapture",
                CaptureBackend: "FakeCapture",
                TargetProfileName: "screen-profile",
                RegionLayoutProfile: "DefaultDesktopGrid",
                LocatorSetName: "DefaultScreenRegionResolutionService",
                LocatorName: "DefaultDesktopGridRegionLocator",
                TargetImageWidth: 1600,
                TargetImageHeight: 900,
                TotalRegionsRequested: 3,
                MatchedRegionCount: 3,
                MissingRegionCount: 0,
                Regions:
                [
                    new ScreenRegionMatch("window.right", "grid", new UiBounds(1080, 0, 520, 900), 0.95d, "locator", "matched", ScreenRegionMatchState.Matched, null, 1600, 900, new Dictionary<string, string?>()),
                    new ScreenRegionMatch("window.top", "grid", new UiBounds(0, 0, 1600, 200), 0.95d, "locator", "matched", ScreenRegionMatchState.Matched, null, 1600, 900, new Dictionary<string, string?>()),
                    new ScreenRegionMatch("window.center", "grid", new UiBounds(300, 180, 1000, 500), 0.95d, "locator", "matched", ScreenRegionMatchState.Matched, null, 1600, 900, new Dictionary<string, string?>())
                ],
                Warnings: [],
                Errors: [],
                Metadata: new Dictionary<string, string?>()),
            CancellationToken.None);

        await stores.PreprocessingStore.UpsertLatestAsync(
            sessionId,
            new SessionFramePreprocessingResult(
                sessionId,
                now,
                SourceSnapshotSequence: 10,
                SourceSnapshotCapturedAtUtc: now,
                SourceRegionResolutionSequence: 10,
                SourceRegionResolutionResolvedAtUtc: now,
                TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
                ObservabilityBackend: "ScreenCapture",
                CaptureBackend: "FakeCapture",
                PreprocessingProfileName: "DefaultFramePreprocessing",
                TotalArtifactCount: 1,
                SuccessfulArtifactCount: 1,
                FailedArtifactCount: 0,
                Artifacts:
                [
                    new ProcessedFrameArtifact(
                        "region:window.right.threshold",
                        "threshold",
                        SourceSnapshotSequence: 10,
                        SourceRegionName: "window.right",
                        OutputWidth: 520,
                        OutputHeight: 900,
                        ImageFormat: "image/png",
                        PayloadByteLength: 3,
                        PreprocessingSteps: ["crop", "threshold"],
                        Warnings: [],
                        Errors: [],
                        Metadata: new Dictionary<string, string?>(),
                        ImageBytes: [7, 8, 9])
                ],
                Warnings: [],
                Errors: [],
                Metadata: new Dictionary<string, string?>()),
            CancellationToken.None);

        if (ocrArtifacts is not null)
        {
            await stores.OcrStore.UpsertLatestAsync(
                sessionId,
                new SessionOcrExtractionResult(
                    sessionId,
                    now,
                    SourceSnapshotSequence: 10,
                    SourceSnapshotCapturedAtUtc: now,
                    SourceRegionResolutionSequence: 10,
                    SourceRegionResolutionResolvedAtUtc: now,
                    SourcePreprocessingProcessedAtUtc: now,
                    TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
                    ObservabilityBackend: "ScreenCapture",
                    CaptureBackend: "FakeCapture",
                    OcrProfileName: "DefaultRegionOcrWithFullFrameFallback",
                    OcrEngineName: "FakeOcr",
                    OcrEngineBackend: "Fake",
                    TotalArtifactCount: ocrArtifacts.Count,
                    SuccessfulArtifactCount: ocrArtifacts.Count,
                    FailedArtifactCount: 0,
                    Artifacts: ocrArtifacts,
                    Warnings: [],
                    Errors: [],
                    Metadata: new Dictionary<string, string?>()),
                CancellationToken.None);
        }

        await stores.TemplateStore.UpsertLatestAsync(
            sessionId,
            new SessionTemplateDetectionResult(
                sessionId,
                now,
                SourceSnapshotSequence: 10,
                SourceSnapshotCapturedAtUtc: now,
                SourceRegionResolutionSequence: 10,
                SourceRegionResolutionResolvedAtUtc: now,
                SourcePreprocessingProcessedAtUtc: now,
                SourceOcrExtractedAtUtc: now,
                TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
                ObservabilityBackend: "ScreenCapture",
                CaptureBackend: "FakeCapture",
                DetectionProfileName: "DefaultRegionTemplateDetection",
                TemplateSetName: "DefaultGenericMarkers",
                MatcherName: "Deterministic",
                MatcherBackend: "Deterministic",
                TotalArtifactCount: 1,
                TotalTemplatesEvaluated: templateMatches.Count,
                SuccessfulArtifactCount: 1,
                FailedArtifactCount: 0,
                Artifacts:
                [
                    new TemplateArtifactResult(
                        "region:window.right.threshold",
                        "window.right",
                        "threshold",
                        "DefaultRegionAwareTemplateSelectionV1",
                        false,
                        EvaluatedTemplateCount: templateMatches.Count,
                        MatchedTemplateCount: templateMatches.Count,
                        Matches: templateMatches,
                        Warnings: [],
                        Errors: [],
                        Metadata: new Dictionary<string, string?>())
                ],
                Warnings: [],
                Errors: [],
                Metadata: new Dictionary<string, string?>()),
            CancellationToken.None);
    }

    private static OcrArtifactResult CreateOcrArtifact(
        string artifactName,
        string regionName,
        string artifactKind,
        double confidence,
        params string[] lines)
    {
        var ocrLines = lines
            .Select(line => new OcrTextLine(line, line, confidence, null, artifactName, regionName))
            .ToArray();

        return new OcrArtifactResult(
            artifactName,
            regionName,
            artifactKind,
            ["crop", artifactKind],
            string.Join("\n", lines),
            string.Join("\n", lines),
            confidence,
            FragmentCount: ocrLines.Length,
            LineCount: ocrLines.Length,
            SelectionStrategy: "DefaultRegionAwareOcrSelectionV1",
            UsedFullFrameFallback: false,
            Fragments: ocrLines.Select(line => new OcrTextFragment(line.Text, line.NormalizedText, line.Confidence, line.Bounds, line.SourceArtifactName, line.SourceRegionName)).ToArray(),
            Lines: ocrLines,
            Warnings: [],
            Errors: [],
            Metadata: new Dictionary<string, string?>());
    }

    private static TargetSemanticPackageContext CreateScreenPackageContext(SessionId sessionId, IReadOnlyDictionary<string, string?>? metadata)
    {
        var now = DateTimeOffset.Parse("2026-04-16T10:00:00Z");
        var mergedMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName,
            ["ObservabilityBackend"] = "ScreenCapture"
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                mergedMetadata[key] = value;
            }
        }

        var definition = new SessionDefinition(
            sessionId,
            "Screen semantic test",
            Enabled: true,
            TickInterval: TimeSpan.FromSeconds(1),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 1,
            InitialBackoff: TimeSpan.FromMilliseconds(10),
            Tags: []);
        var runtime = SessionRuntimeState.Create(definition, now) with
        {
            CurrentStatus = SessionStatus.Running,
            DesiredStatus = SessionStatus.Running
        };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);

        var profile = new DesktopTargetProfile(
            "screen-profile",
            DesktopTargetKind.ScreenCaptureDesktop,
            "ScreenApp",
            WindowTitleFragment: null,
            CommandLineFragmentTemplate: null,
            BaseAddressTemplate: null,
            DesktopSessionMatchingMode.WindowTitle,
            mergedMetadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: false);

        var target = new DesktopSessionTarget(
            sessionId,
            "screen-profile",
            DesktopTargetKind.ScreenCaptureDesktop,
            DesktopSessionMatchingMode.WindowTitle,
            "ScreenApp",
            WindowTitleFragment: null,
            CommandLineFragment: null,
            BaseAddress: null,
            mergedMetadata);

        var tree = new UiTree(
            new UiSnapshotMetadata("screen", "ScreenFixture", now, 111, 999, "EVE", new Dictionary<string, string?>()),
            new UiNode(new UiNodeId("root"), "Window", "ScreenRoot", "EVE", null, true, true, false, [], []));

        var semanticContext = new UiSemanticExtractionContext(
            sessionId,
            SessionUiState.Create(sessionId) with
            {
                ProjectedTree = tree,
                RawSnapshotJson = "{}",
                LastSnapshotCapturedAtUtc = now
            },
            tree,
            SessionDomainState.CreateBootstrap(sessionId, now.AddMinutes(-1)),
            snapshot,
            new ResolvedDesktopTargetContext(
                sessionId,
                profile,
                new SessionTargetBinding(sessionId, "screen-profile", new Dictionary<string, string>(), null),
                target,
                new Dictionary<string, string>()),
            Attachment: null,
            now);

        return new TargetSemanticPackageContext(semanticContext, UiSemanticExtractionResult.Empty(sessionId, now));
    }

    private sealed record VisualStores(
        ISessionScreenSnapshotStore SnapshotStore,
        ISessionScreenRegionStore RegionStore,
        ISessionFramePreprocessingStore PreprocessingStore,
        ISessionOcrExtractionStore OcrStore,
        ISessionTemplateDetectionStore TemplateStore);
}
