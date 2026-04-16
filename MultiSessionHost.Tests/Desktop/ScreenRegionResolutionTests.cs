using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class ScreenRegionResolutionTests
{
    [Fact]
    public async Task DefaultDesktopGridRegionLocator_ResolvesDeterministicRegionsFromSnapshot()
    {
        var sessionId = new SessionId("alpha");
        var snapshot = CreateSnapshot(sessionId, sequence: 1, width: 800, height: 600);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", "DefaultDesktopGrid");
        var locator = new DefaultDesktopGridRegionLocator();

        var result = await locator.ResolveAsync(snapshot, context, "DefaultDesktopGrid", DateTimeOffset.UtcNow, CancellationToken.None);

        var full = Assert.Single(result.RegionSet.Regions, static region => region.RegionName == "window.full");
        var top = Assert.Single(result.RegionSet.Regions, static region => region.RegionName == "window.top");
        var center = Assert.Single(result.RegionSet.Regions, static region => region.RegionName == "window.center");

        Assert.Equal(new UiBounds(0, 0, 800, 600), full.Bounds);
        Assert.Equal(new UiBounds(0, 0, 800, 72), top.Bounds);
        Assert.Equal(ScreenRegionMatchState.Inferred, center.MatchState);
        Assert.Equal(7, result.RegionSet.Regions.Count);
    }

    [Fact]
    public async Task InMemorySessionScreenRegionStore_UpsertsAndReturnsLatestResolution()
    {
        var sessionId = new SessionId("alpha");
        var store = new InMemorySessionScreenRegionStore();
        var first = await CreateResolutionAsync(sessionId, sequence: 1, width: 800, height: 600, layoutProfile: "DefaultDesktopGrid");
        var second = await CreateResolutionAsync(sessionId, sequence: 2, width: 1024, height: 768, layoutProfile: "DefaultDesktopGrid");

        await store.UpsertLatestAsync(sessionId, first, CancellationToken.None);
        await store.UpsertLatestAsync(sessionId, second, CancellationToken.None);

        var latest = await store.GetLatestAsync(sessionId, CancellationToken.None);
        var summary = await store.GetLatestSummaryAsync(sessionId, CancellationToken.None);
        var allLatest = await store.GetAllLatestAsync(CancellationToken.None);
        var allSummaries = await store.GetAllLatestSummariesAsync(CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(2, latest!.SourceSnapshotSequence);
        Assert.Equal(1024, latest.TargetImageWidth);
        Assert.Single(allLatest);
        Assert.Single(allSummaries);
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.SourceSnapshotSequence);
    }

    [Fact]
    public void LocatorSelection_CanVaryByProfileMetadata()
    {
        var resolver = new DefaultScreenRegionLocatorResolver([new DefaultDesktopGridRegionLocator(), new InsetDesktopGridRegionLocator()]);
        var snapshot = CreateSnapshot(new SessionId("alpha"), sequence: 1, width: 800, height: 600);

        var defaultContext = CreateContext(new SessionId("alpha"), DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", "DefaultDesktopGrid");
        var insetContext = CreateContext(new SessionId("alpha"), DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", "InsetDesktopGrid");

        Assert.Equal("DefaultDesktopGridRegionLocator", resolver.Resolve(defaultContext, snapshot, "DefaultDesktopGrid").LocatorName);
        Assert.Equal("InsetDesktopGridRegionLocator", resolver.Resolve(insetContext, snapshot, "InsetDesktopGrid").LocatorName);
    }

    [Fact]
    public async Task RegionResolutionService_DoesNotPopulateTheRegionStoreForUiaTargets()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var service = CreateService(snapshotStore, regionStore, [new DefaultDesktopGridRegionLocator()]);
        var context = CreateContext(sessionId, DesktopTargetKind.WindowsUiAutomationDesktop, "uia-profile", "DefaultDesktopGrid");

        var result = await service.ResolveLatestAsync(sessionId, context, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(await regionStore.GetLatestAsync(sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task RegionResolutionFailure_IsRecordedAndLeavesTheScreenSnapshotIntact()
    {
        var sessionId = new SessionId("alpha");
        var snapshotStore = new InMemorySessionScreenSnapshotStore(new SessionHostOptions());
        var regionStore = new InMemorySessionScreenRegionStore();
        var snapshot = CreateSnapshot(sessionId, sequence: 1, width: 800, height: 600);
        await snapshotStore.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None);
        var service = CreateService(snapshotStore, regionStore, [new ThrowingScreenRegionLocator()]);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", "ThrowingLayout");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveLatestAsync(sessionId, context, CancellationToken.None).AsTask());

        var latestSnapshot = await snapshotStore.GetLatestAsync(sessionId, CancellationToken.None);
        var latestRegion = await regionStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.Contains("deterministically", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(latestSnapshot);
        Assert.NotNull(latestRegion);
        Assert.Contains(latestRegion!.Errors, error => error.Contains("deterministically", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, latestSnapshot!.Sequence);
    }

    private static DefaultScreenRegionResolutionService CreateService(
        ISessionScreenSnapshotStore snapshotStore,
        ISessionScreenRegionStore regionStore,
        IReadOnlyCollection<IScreenRegionLocator> locators) =>
        new(
            snapshotStore,
            regionStore,
            new DefaultScreenRegionLocatorResolver(locators),
            NullLogger<DefaultScreenRegionResolutionService>.Instance);

    private static async Task<SessionScreenRegionResolution> CreateResolutionAsync(
        SessionId sessionId,
        long sequence,
        int width,
        int height,
        string layoutProfile)
    {
        var snapshot = CreateSnapshot(sessionId, sequence, width, height);
        var context = CreateContext(sessionId, DesktopTargetKind.ScreenCaptureDesktop, "screen-profile", layoutProfile);
        var locator = layoutProfile == "InsetDesktopGrid"
            ? new InsetDesktopGridRegionLocator() as IScreenRegionLocator
            : new DefaultDesktopGridRegionLocator();

        var locatorResult = await locator.ResolveAsync(snapshot, context, layoutProfile, DateTimeOffset.UtcNow, CancellationToken.None);

        return new SessionScreenRegionResolution(
            snapshot.SessionId,
            locatorResult.ResolvedAtUtc,
            snapshot.Sequence,
            snapshot.CapturedAtUtc,
            snapshot.TargetKind,
            snapshot.ObservabilityBackend,
            snapshot.CaptureBackend,
            context.Profile.ProfileName,
            layoutProfile,
            nameof(DefaultScreenRegionResolutionService),
            locatorResult.LocatorName,
            snapshot.ImageWidth,
            snapshot.ImageHeight,
            locatorResult.RegionSet.Regions.Count,
            locatorResult.RegionSet.Regions.Count(static region => region.MatchState != ScreenRegionMatchState.Missing),
            locatorResult.RegionSet.Regions.Count(static region => region.MatchState == ScreenRegionMatchState.Missing),
            locatorResult.RegionSet.Regions,
            locatorResult.Warnings,
            locatorResult.Errors,
            locatorResult.Metadata);
    }

    private static SessionScreenSnapshot CreateSnapshot(SessionId sessionId, long sequence, int width, int height)
    {
        var payload = new byte[] { 10, 20, 30 };
        var screenSnapshot = new ScreenSnapshot(
            sessionId.Value,
            DateTimeOffset.UtcNow,
            321,
            "ScreenApp",
            999,
            "Screen Fixture",
            new UiBounds(50, 60, width, height),
            width,
            height,
            "image/png",
            "Format32bppArgb",
            payload,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["captureSource"] = "ScreenCapture",
                ["observabilityBackend"] = "ScreenCapture",
                ["captureBackend"] = "FakeCapture",
                ["targetKind"] = DesktopTargetKind.ScreenCaptureDesktop.ToString()
            });

        return SessionScreenSnapshot.FromScreenSnapshot(screenSnapshot, DesktopTargetKind.ScreenCaptureDesktop, sequence);
    }

    private static ResolvedDesktopTargetContext CreateContext(SessionId sessionId, DesktopTargetKind kind, string profileName, string regionLayoutProfile)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RegionLayoutProfile"] = regionLayoutProfile,
            ["ObservabilityBackend"] = "ScreenCapture"
        };

        var profile = new DesktopTargetProfile(
            profileName,
            kind,
            "ScreenApp",
            null,
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            metadata,
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
            metadata);

        return new ResolvedDesktopTargetContext(
            sessionId,
            profile,
            new SessionTargetBinding(sessionId, profileName, new Dictionary<string, string>(StringComparer.Ordinal), Overrides: null),
            target,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private sealed class ThrowingScreenRegionLocator : IScreenRegionLocator
    {
        public string LocatorName => nameof(ThrowingScreenRegionLocator);

        public string RegionLayoutProfile => "ThrowingLayout";

        public bool Supports(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile) =>
            context.Target.Kind == DesktopTargetKind.ScreenCaptureDesktop && string.Equals(regionLayoutProfile, RegionLayoutProfile, StringComparison.OrdinalIgnoreCase);

        public ValueTask<ScreenRegionLocatorResult> ResolveAsync(SessionScreenSnapshot snapshot, ResolvedDesktopTargetContext context, string regionLayoutProfile, DateTimeOffset resolvedAtUtc, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The region locator failed deterministically.");
    }
}