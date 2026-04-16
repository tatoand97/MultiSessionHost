using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Infrastructure.DependencyInjection;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class ScreenCaptureUiRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_SucceedsForScreenCaptureTargetWithoutProjectedTree()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            [10, 20, 30],
            "FakeCapture");

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
        services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        await coordinator.InitializeAsync(CancellationToken.None);

        var sessionId = new SessionId("alpha");
        var snapshot = coordinator.GetSession(sessionId)!;
        var context = provider.GetRequiredService<IDesktopTargetProfileResolver>().Resolve(snapshot);
        var attachment = new DesktopSessionAttachment(sessionId, context.Target, process, window, null, clock.UtcNow);
        var refreshService = provider.GetRequiredService<ISessionUiRefreshService>();
        var screenSnapshotStore = provider.GetRequiredService<ISessionScreenSnapshotStore>();
        var preprocessingStore = provider.GetRequiredService<ISessionFramePreprocessingStore>();

        var state = await refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None);
        var storedSnapshot = await screenSnapshotStore.GetLatestAsync(sessionId, CancellationToken.None);
        var summary = await screenSnapshotStore.GetLatestSummaryAsync(sessionId, CancellationToken.None);
        var regionStore = provider.GetRequiredService<ISessionScreenRegionStore>();
        var regionResolution = await regionStore.GetLatestAsync(sessionId, CancellationToken.None);
        var regionSummary = await regionStore.GetLatestSummaryAsync(sessionId, CancellationToken.None);
        var preprocessing = await preprocessingStore.GetLatestAsync(sessionId, CancellationToken.None);
        var preprocessingSummary = await preprocessingStore.GetLatestSummaryAsync(sessionId, CancellationToken.None);

        Assert.NotNull(state.RawSnapshotJson);
        Assert.Null(state.ProjectedTree);
        Assert.Null(state.LastDiff);
        Assert.Empty(state.PlannedWorkItems);
        Assert.NotNull(state.LastRefreshCompletedAtUtc);

        using var document = JsonDocument.Parse(state.RawSnapshotJson!);
        Assert.Equal("ScreenCapture", document.RootElement.GetProperty("metadata").GetProperty("captureSource").GetString());
        Assert.Equal("ScreenCaptureDesktop", document.RootElement.GetProperty("metadata").GetProperty("targetKind").GetString());
        Assert.Equal("ScreenCaptureDesktopTargetAdapter", document.RootElement.GetProperty("metadata").GetProperty("adapter").GetString());
        Assert.Equal(800, document.RootElement.GetProperty("root").GetProperty("imageWidth").GetInt32());
        Assert.Equal(600, document.RootElement.GetProperty("root").GetProperty("imageHeight").GetInt32());
        Assert.Equal("ScreenCaptureDesktop", document.RootElement.GetProperty("root").GetProperty("metadata").GetProperty("targetKind").GetString());
        Assert.NotNull(storedSnapshot);
        Assert.NotNull(summary);
        Assert.NotNull(regionResolution);
        Assert.NotNull(regionSummary);
        Assert.Equal(sessionId, storedSnapshot!.SessionId);
        Assert.Equal(800, storedSnapshot.ImageWidth);
        Assert.Equal(3, storedSnapshot.PayloadByteLength);
        Assert.Equal("FakeCapture", storedSnapshot.CaptureBackend);
        Assert.Equal("ScreenCapture", summary!.CaptureSource);
        Assert.Equal(3, summary.PayloadByteLength);
        Assert.Equal("DefaultScreenRegionResolutionService", regionResolution!.LocatorSetName);
        Assert.Equal("DefaultDesktopGridRegionLocator", regionResolution.LocatorName);
        Assert.Equal(7, regionResolution.TotalRegionsRequested);
        Assert.Equal(5, regionResolution.MatchedRegionCount);
        Assert.Equal(0, regionResolution.MissingRegionCount);
        Assert.Equal("window.full", regionResolution.Regions[0].RegionName);
        Assert.Equal("DefaultDesktopGridRegionLocator", regionSummary!.LocatorName);
        Assert.Equal(7, regionSummary.TotalRegionsRequested);
        Assert.NotNull(preprocessing);
        Assert.NotNull(preprocessingSummary);
        Assert.True(preprocessing!.TotalArtifactCount >= 3);
        Assert.True(preprocessing.SuccessfulArtifactCount >= 1);
        Assert.Equal(storedSnapshot.Sequence, preprocessing.SourceSnapshotSequence);
    }

    [Fact]
    public async Task RefreshAsync_FailsClearlyWhenAttachedWindowIsMissing()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window: null));
        services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(
            new WindowFrameCaptureResult(new UiBounds(0, 0, 1, 1), 1, 1, "image/png", "Format32bppArgb", [1], "FakeCapture")));
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        await coordinator.InitializeAsync(CancellationToken.None);

        var sessionId = new SessionId("alpha");
        var snapshot = coordinator.GetSession(sessionId)!;
        var context = provider.GetRequiredService<IDesktopTargetProfileResolver>().Resolve(snapshot);
        var attachment = new DesktopSessionAttachment(sessionId, context.Target, process, window, null, clock.UtcNow);
        var refreshService = provider.GetRequiredService<ISessionUiRefreshService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None));

        Assert.Contains("window", exception.Message, StringComparison.OrdinalIgnoreCase);

        var uiState = coordinator.GetSessionUiState(sessionId)!;
        Assert.Contains("window", uiState.LastRefreshError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScreenSnapshotStore_PreservesLastKnownSnapshotAcrossStopAndRestartUntilReplaced()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
        services.AddSingleton<IWindowFrameCapture>(new SequenceWindowFrameCapture(
            new WindowFrameCaptureResult(new UiBounds(50, 60, 800, 600), 800, 600, "image/png", "Format32bppArgb", [10, 20, 30], "FakeCapture"),
            new WindowFrameCaptureResult(new UiBounds(50, 60, 800, 600), 800, 600, "image/png", "Format32bppArgb", [40, 50], "FakeCapture")));
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        var store = provider.GetRequiredService<ISessionScreenSnapshotStore>();
        var sessionId = new SessionId("alpha");

        await coordinator.InitializeAsync(CancellationToken.None);
        await coordinator.StartSessionAsync(sessionId, CancellationToken.None);

        var firstRefresh = await coordinator.RefreshSessionUiAsync(sessionId, CancellationToken.None);
        var firstSnapshot = await store.GetLatestAsync(sessionId, CancellationToken.None);

        await coordinator.StopSessionAsync(sessionId, CancellationToken.None);

        var snapshotAfterStop = await store.GetLatestAsync(sessionId, CancellationToken.None);

        await coordinator.StartSessionAsync(sessionId, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        var secondRefresh = await coordinator.RefreshSessionUiAsync(sessionId, CancellationToken.None);
        var secondSnapshot = await store.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.NotNull(firstRefresh.RawSnapshotJson);
        Assert.NotNull(secondRefresh.RawSnapshotJson);
        Assert.NotNull(firstSnapshot);
        Assert.NotNull(snapshotAfterStop);
        Assert.NotNull(secondSnapshot);
        Assert.Equal(firstSnapshot!.Sequence, snapshotAfterStop!.Sequence);
        Assert.Equal(firstSnapshot.PayloadByteLength, snapshotAfterStop.PayloadByteLength);
        Assert.Equal(firstSnapshot.Sequence + 1, secondSnapshot!.Sequence);
        Assert.Equal(2, secondSnapshot.PayloadByteLength);
    }

    [Fact]
    public async Task RefreshAsync_PreprocessingFailure_DoesNotCorruptSnapshotOrRegionState()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            [10, 20, 30],
            "FakeCapture");

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
        services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
        services.AddSingleton<IFramePreprocessingService, ThrowingFramePreprocessingService>();
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        await coordinator.InitializeAsync(CancellationToken.None);

        var sessionId = new SessionId("alpha");
        var snapshot = coordinator.GetSession(sessionId)!;
        var context = provider.GetRequiredService<IDesktopTargetProfileResolver>().Resolve(snapshot);
        var attachment = new DesktopSessionAttachment(sessionId, context.Target, process, window, null, clock.UtcNow);
        var refreshService = provider.GetRequiredService<ISessionUiRefreshService>();
        var screenSnapshotStore = provider.GetRequiredService<ISessionScreenSnapshotStore>();
        var regionStore = provider.GetRequiredService<ISessionScreenRegionStore>();

        var state = await refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None);
        var storedSnapshot = await screenSnapshotStore.GetLatestAsync(sessionId, CancellationToken.None);
        var regionResolution = await regionStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.NotNull(state.LastRefreshCompletedAtUtc);
        Assert.NotNull(storedSnapshot);
        Assert.NotNull(regionResolution);
        Assert.Equal(1, storedSnapshot!.Sequence);
        Assert.Equal(1, regionResolution!.SourceSnapshotSequence);
    }

    [Fact]
    public async Task RefreshAsync_StoresLatestOcrResult_ForScreenCaptureTargets()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            [10, 20, 30],
            "FakeCapture");

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
        services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        await coordinator.InitializeAsync(CancellationToken.None);

        var sessionId = new SessionId("alpha");
        var snapshot = coordinator.GetSession(sessionId)!;
        var context = provider.GetRequiredService<IDesktopTargetProfileResolver>().Resolve(snapshot);
        var attachment = new DesktopSessionAttachment(sessionId, context.Target, process, window, null, clock.UtcNow);
        var refreshService = provider.GetRequiredService<ISessionUiRefreshService>();
        var ocrStore = provider.GetRequiredService<ISessionOcrExtractionStore>();

        _ = await refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None);
        var ocrResult = await ocrStore.GetLatestAsync(sessionId, CancellationToken.None);
        var ocrSummary = await ocrStore.GetLatestSummaryAsync(sessionId, CancellationToken.None);

        Assert.NotNull(ocrResult);
        Assert.NotNull(ocrSummary);
        Assert.Equal("DefaultRegionOcrWithFullFrameFallback", ocrResult!.OcrProfileName);
        Assert.True(ocrResult.TotalArtifactCount >= 1);
        Assert.Equal(ocrResult.SourceSnapshotSequence, ocrSummary!.SourceSnapshotSequence);
    }

    [Fact]
    public async Task RefreshAsync_OcrFailure_DoesNotCorruptSnapshotRegionOrPreprocessingState()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            [10, 20, 30],
            "FakeCapture");

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddMultiSessionHostRuntime();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
        services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
        services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
        services.AddSingleton<IOcrExtractionService, ThrowingOcrExtractionService>();
        await using var provider = services.BuildServiceProvider();

        var coordinator = provider.GetRequiredService<ISessionCoordinator>();
        await coordinator.InitializeAsync(CancellationToken.None);

        var sessionId = new SessionId("alpha");
        var snapshot = coordinator.GetSession(sessionId)!;
        var context = provider.GetRequiredService<IDesktopTargetProfileResolver>().Resolve(snapshot);
        var attachment = new DesktopSessionAttachment(sessionId, context.Target, process, window, null, clock.UtcNow);
        var refreshService = provider.GetRequiredService<ISessionUiRefreshService>();
        var snapshotStore = provider.GetRequiredService<ISessionScreenSnapshotStore>();
        var regionStore = provider.GetRequiredService<ISessionScreenRegionStore>();
        var preprocessingStore = provider.GetRequiredService<ISessionFramePreprocessingStore>();

        var state = await refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None);
        var storedSnapshot = await snapshotStore.GetLatestAsync(sessionId, CancellationToken.None);
        var regionResolution = await regionStore.GetLatestAsync(sessionId, CancellationToken.None);
        var preprocessing = await preprocessingStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.NotNull(state.LastRefreshCompletedAtUtc);
        Assert.NotNull(storedSnapshot);
        Assert.NotNull(regionResolution);
        Assert.NotNull(preprocessing);
        Assert.Equal(1, storedSnapshot!.Sequence);
        Assert.Equal(1, regionResolution!.SourceSnapshotSequence);
        Assert.Equal(1, preprocessing!.SourceSnapshotSequence);
    }


    private static SessionHostOptions CreateOptions() =>
        new()
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            RuntimePersistence = new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = false,
                AutoFlushAfterStateChanges = false
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)],
            DesktopTargets =
            [
                new DesktopTargetProfileOptions
                {
                    ProfileName = "screen-profile",
                    Kind = DesktopTargetKind.ScreenCaptureDesktop,
                    ProcessName = "ScreenApp",
                    WindowTitleFragment = "Screen Fixture",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitle,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = false,
                    Metadata =
                    {
                        ["UiSource"] = "ScreenCapture",
                        ["ObservabilityBackend"] = "ScreenCapture",
                        ["EnableOcr"] = true.ToString(),
                        ["OcrProfile"] = "DefaultRegionOcrWithFullFrameFallback",
                        ["OcrIncludeFullFrameFallback"] = true.ToString()
                    }
                }
            ],
            SessionTargetBindings =
            [
                new SessionTargetBindingOptions
                {
                    SessionId = "alpha",
                    TargetProfileName = "screen-profile"
                }
            ]
        };

    private sealed class StubWindowFrameCapture : IWindowFrameCapture
    {
        private readonly WindowFrameCaptureResult _result;

        public StubWindowFrameCapture(WindowFrameCaptureResult result)
        {
            _result = result;
        }

        public Task<WindowFrameCaptureResult> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class SequenceWindowFrameCapture : IWindowFrameCapture
    {
        private readonly Queue<WindowFrameCaptureResult> _results;

        public SequenceWindowFrameCapture(params WindowFrameCaptureResult[] results)
        {
            _results = new Queue<WindowFrameCaptureResult>(results);
        }

        public Task<WindowFrameCaptureResult> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No more capture results were configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly DesktopProcessInfo? _process;

        public StubProcessLocator(DesktopProcessInfo? process)
        {
            _process = process;
        }

        public IReadOnlyCollection<DesktopProcessInfo> GetProcesses(string? processName = null) =>
            _process is null ? [] : [_process];

        public DesktopProcessInfo? GetProcessById(int processId) =>
            _process is not null && _process.ProcessId == processId ? _process : null;
    }

    private sealed class StubWindowLocator : IWindowLocator
    {
        private readonly DesktopWindowInfo? _window;

        public StubWindowLocator(DesktopWindowInfo? window)
        {
            _window = window;
        }

        public IReadOnlyCollection<DesktopWindowInfo> GetWindows() =>
            _window is null ? [] : [_window];

        public DesktopWindowInfo? GetWindowByHandle(long handle) =>
            _window is not null && _window.WindowHandle == handle ? _window : null;
    }

    private sealed class ThrowingFramePreprocessingService : IFramePreprocessingService
    {
        public ValueTask<SessionFramePreprocessingResult?> PreprocessLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Frame preprocessing failed deterministically.");
    }

    private sealed class ThrowingOcrExtractionService : IOcrExtractionService
    {
        public ValueTask<SessionOcrExtractionResult?> ExtractLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("OCR extraction failed deterministically.");
    }
}
