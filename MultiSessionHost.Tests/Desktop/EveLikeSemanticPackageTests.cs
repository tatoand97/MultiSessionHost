using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.AdminApi.Mapping;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class EveLikeSemanticPackageTests
{
    [Fact]
    public async Task Resolver_SelectsPackageFromProfileMetadata()
    {
        var (_, context, _, _) = CreateHarness(withPackageMetadata: true, registerPackage: true);
        var resolver = new DefaultTargetSemanticPackageResolver([new EveLikeSemanticPackage(new DefaultUiTreeQueryService())]);

        var selection = resolver.ResolveSelection(context.TargetContext);
        var package = resolver.ResolvePackage(selection!.PackageName);

        Assert.Equal("EveLike", selection!.PackageName);
        Assert.Equal(EveLikeSemanticPackage.SemanticPackageVersion, package!.PackageVersion);
    }

    [Fact]
    public async Task Pipeline_ExtractsPackageAndEmitsObservabilityEvents()
    {
        var (pipeline, context, store, _) = CreateHarness(withPackageMetadata: true, registerPackage: true);

        var result = await pipeline.ExtractAsync(context, CancellationToken.None);

        Assert.NotEmpty(result.Lists);
        Assert.Single(result.Packages);

        var package = Assert.Single(result.Packages);
        Assert.True(package.Succeeded);
        Assert.NotNull(package.EveLike);
        Assert.Equal("EveLike", package.PackageName);
        Assert.Contains(package.EveLike!.Presence.Entities, entity => entity.Label == "Local Alpha" && entity.Standing == "Friendly");
        Assert.Contains(package.EveLike.OverviewEntries, entry => entry.Label == "Enemy Battleship" && entry.Targeted);
        Assert.Contains(package.EveLike.ProbeScannerEntries, entry => entry.Label == "Anomaly Site" && entry.SignatureType == "Anomaly");
        Assert.True(package.EveLike.Safety.IsSafeLocation);

        var events = await store.GetEventsAsync(context.SessionId, CancellationToken.None);
        Assert.Contains(events, static item => item.EventType == "semantic.package.selected");
        Assert.Contains(events, static item => item.EventType == "semantic.package.started");
        Assert.Contains(events, static item => item.EventType == "semantic.package.succeeded");
    }

    [Fact]
    public async Task Pipeline_LeavesGenericExtractionIntactWhenNoPackageConfigured()
    {
        var (pipeline, context, _, _) = CreateHarness(withPackageMetadata: false, registerPackage: false);

        var result = await pipeline.ExtractAsync(context, CancellationToken.None);

        Assert.NotEmpty(result.Lists);
        Assert.Empty(result.Packages);
        Assert.Contains(result.PresenceEntities, entity => entity.Label == "Local" && entity.Count == 3);
    }

    [Fact]
    public async Task RiskBuilder_UsesPackageSemanticsToCreateSpecificCandidates()
    {
        var (pipeline, context, _, _) = CreateHarness(withPackageMetadata: true, registerPackage: true);
        var result = await pipeline.ExtractAsync(context, CancellationToken.None);
        var builder = new DefaultRiskCandidateBuilder();

        var candidates = builder.BuildCandidates(result);

        Assert.Contains(candidates, candidate => candidate.CandidateId.Contains("package:EveLike:overview", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, candidate => candidate.Metadata.TryGetValue("packageName", out var packageName) && packageName == "EveLike");
        Assert.Contains(candidates, candidate => candidate.Name == "Enemy Battleship" || candidate.Name.Contains("safety", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DomainProjection_UsesPackageNavigationThreatAndSafetySignals()
    {
        var (pipeline, context, _, snapshot) = CreateHarness(withPackageMetadata: true, registerPackage: true);
        var result = await pipeline.ExtractAsync(context, CancellationToken.None);
        var now = DateTimeOffset.Parse("2026-04-15T15:00:00Z");
        var domainState = SessionDomainState.CreateBootstrap(context.SessionId, now.AddMinutes(-1));
        var service = new DefaultSessionDomainStateProjectionService();

        var projected = service.Project(domainState, snapshot, context.TargetContext, context.SessionUiState, attachment: null, result, RiskAssessmentResult.Empty(context.SessionId, context.Now), context.Now);

        Assert.Equal("Jita", projected.Navigation.DestinationLabel);
        Assert.Equal("Amarr", projected.Navigation.RouteLabel);
        Assert.Equal("Docked at station", projected.Location.ContextLabel);
        Assert.True(projected.Location.Confidence is LocationConfidence.High or LocationConfidence.Medium);
        Assert.True(projected.Threat.IsSafe);
        Assert.Contains(projected.Threat.Signals, signal => signal.Contains("Package overview entries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticDto_SurfaceIncludesPackageOutput()
    {
        var (pipeline, context, _, _) = CreateHarness(withPackageMetadata: true, registerPackage: true);
        var result = await pipeline.ExtractAsync(context, CancellationToken.None);
        var dto = result.ToDto();

        Assert.Single(dto.Packages);
        Assert.Equal("EveLike", dto.Packages[0].PackageName);
        Assert.NotNull(dto.Packages[0].EveLike);
        Assert.Equal(3, dto.Packages[0].EveLike!.Presence.Entities.Count);
    }

    private static (UiSemanticExtractionPipeline Pipeline, UiSemanticExtractionContext Context, InMemorySessionObservabilityStore Store, SessionSnapshot Snapshot) CreateHarness(bool withPackageMetadata, bool registerPackage)
    {
        var sessionId = new SessionId("eve-like-session");
        var now = DateTimeOffset.Parse("2026-04-15T15:00:00Z");
        var tree = CreateTree();
        var definition = new SessionDefinition(
            sessionId,
            "Eve Like Session",
            Enabled: true,
            TickInterval: TimeSpan.FromSeconds(1),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 3,
            InitialBackoff: TimeSpan.FromMilliseconds(100),
            Tags: ["test"]);
        var runtime = SessionRuntimeState.Create(definition, now) with
        {
            CurrentStatus = SessionStatus.Running,
            DesiredStatus = SessionStatus.Running
        };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);
        var profileMetadata = new Dictionary<string, string?>
        {
            ["UiSource"] = "EveLikeFixture"
        };

        if (withPackageMetadata)
        {
            profileMetadata["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName;
        }

        var profile = new DesktopTargetProfile(
            "eve-like-profile",
            DesktopTargetKind.WindowsUiAutomationDesktop,
            "EveExe",
            "EVE Fixture",
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            profileMetadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: false);
        var binding = new SessionTargetBinding(sessionId, profile.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var target = new DesktopSessionTarget(
            sessionId,
            profile.ProfileName,
            profile.Kind,
            profile.MatchingMode,
            profile.ProcessName,
            WindowTitleFragment: profile.WindowTitleFragment,
            CommandLineFragment: null,
            BaseAddress: null,
            profile.Metadata);

        var context = new UiSemanticExtractionContext(
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
            new ResolvedDesktopTargetContext(sessionId, profile, binding, target, new Dictionary<string, string>()),
            Attachment: null,
            now);

        var query = new DefaultUiTreeQueryService();
        var classifier = new DefaultUiSemanticClassifier();
        var store = new InMemorySessionObservabilityStore(new SessionHostOptions
        {
            Observability = new ObservabilityOptions { EnableObservability = true, EnableMetrics = true }
        });
        var recorder = new DefaultObservabilityRecorder(new SessionHostOptions
        {
            Observability = new ObservabilityOptions { EnableObservability = true, EnableMetrics = true }
        }, store, new FakeClock(now), NullLogger<DefaultObservabilityRecorder>.Instance);

        var packageResolver = registerPackage
            ? new DefaultTargetSemanticPackageResolver([new EveLikeSemanticPackage(query)])
            : new DefaultTargetSemanticPackageResolver([]);

        var pipeline = new UiSemanticExtractionPipeline(
            [
                new ListDetectorExtractor(query, classifier),
                new TargetDetectorExtractor(query, classifier),
                new AlertDetectorExtractor(query, classifier),
                new TransitStateDetectorExtractor(query, classifier),
                new ResourceCapabilityDetectorExtractor(query, classifier),
                new PresenceEntityDetectorExtractor(query, classifier)
            ],
            packageResolver,
            recorder);

        return (pipeline, context, store, snapshot);
    }

    private static UiTree CreateTree() =>
        new(
            new UiSnapshotMetadata("eve-like", "EveLikeFixture", DateTimeOffset.UtcNow, 1, 1, "EVE Fixture", new Dictionary<string, string?>()),
            Node(
                "root",
                "Window",
                text: "EVE Fixture",
                children:
                [
                    Node("localPanel", "ListBox", name: "Local", attributes:
                    [
                        new UiAttribute("semanticRole", "presence"),
                        new UiAttribute("itemCount", "3"),
                        new UiAttribute("items", "[\"Local Alpha\",\"Local Bravo\",\"Local Charlie\"]")
                    ], children:
                    [
                        Node("local-1", "ListItem", text: "Local Alpha", attributes: [new UiAttribute("standing", "Friendly")]),
                        Node("local-2", "ListItem", text: "Local Bravo", attributes: [new UiAttribute("standing", "Neutral")]),
                        Node("local-3", "ListItem", text: "Local Charlie", attributes: [new UiAttribute("standing", "Hostile")])
                    ]),
                    Node("routePanel", "Panel", name: "Route", attributes:
                    [
                        new UiAttribute("destination", "Jita"),
                        new UiAttribute("currentLocation", "Amarr"),
                        new UiAttribute("nextWaypoint", "Perimeter"),
                        new UiAttribute("progressPercent", "40")
                    ], children:
                    [
                        Node("route-1", "ListItem", text: "Niarja"),
                        Node("route-2", "ListItem", text: "Tama"),
                        Node("route-3", "ListItem", text: "Perimeter")
                    ]),
                    Node("overviewPanel", "Table", name: "Overview", attributes:
                    [
                        new UiAttribute("semanticRole", "overview")
                    ], children:
                    [
                        Node("overview-1", "DataItem", text: "Enemy Battleship", attributes:
                        [
                            new UiAttribute("category", "Ship"),
                            new UiAttribute("distanceText", "12.5 km"),
                            new UiAttribute("disposition", "Hostile"),
                            new UiAttribute("targeted", "true")
                        ]),
                        Node("overview-2", "DataItem", text: "Friendly Scout", attributes:
                        [
                            new UiAttribute("category", "Ship"),
                            new UiAttribute("distanceText", "8 km"),
                            new UiAttribute("disposition", "Friendly"),
                            new UiAttribute("selected", "true")
                        ])
                    ]),
                    Node("probePanel", "ListBox", name: "Probe Scanner", attributes:
                    [
                        new UiAttribute("semanticRole", "probe scanner")
                    ], children:
                    [
                        Node("probe-1", "ListItem", text: "Anomaly Site", attributes:
                        [
                            new UiAttribute("signatureType", "Anomaly"),
                            new UiAttribute("distanceText", "3.2 AU"),
                            new UiAttribute("status", "Available")
                        ]),
                        Node("probe-2", "ListItem", text: "Signature Alpha", attributes:
                        [
                            new UiAttribute("signatureType", "Signature"),
                            new UiAttribute("distanceText", "1.2 AU"),
                            new UiAttribute("status", "Scanning")
                        ])
                    ]),
                    Node("safetyPanel", "Label", text: "Docked at station", attributes:
                    [
                        new UiAttribute("semanticRole", "safe-location"),
                        new UiAttribute("safe", "true")
                    ])
                ]));

    private static UiNode Node(
        string id,
        string role,
        string? name = null,
        string? text = null,
        bool visible = true,
        bool enabled = true,
        bool selected = false,
        IReadOnlyList<UiAttribute>? attributes = null,
        IReadOnlyList<UiNode>? children = null) =>
        new(
            new UiNodeId(id),
            role,
            name,
            text,
            Bounds: null,
            visible,
            enabled,
            selected,
            attributes ?? [],
            children ?? []);
}