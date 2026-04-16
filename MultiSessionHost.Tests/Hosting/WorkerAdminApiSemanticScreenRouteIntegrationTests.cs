using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiSemanticScreenRouteIntegrationTests
{
    [Fact]
    public async Task SemanticEndpoints_ExposeScreenBackedEveLikeTravelRoute()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 1280, 720),
            1280,
            720,
            "image/png",
            "Format32bppArgb",
            CreateTestPng(64, 48),
            "FakeCapture");

        await using var harness = await WorkerHostHarness.StartAsync(
            CreateOptions(),
            services =>
            {
                services.AddSingleton<IClock>(clock);
                services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
                services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
                services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
                services.AddSingleton<IOcrEngine, RouteOcrEngine>();
            });

        var client = Assert.IsType<HttpClient>(harness.Client);
        var sessionId = new SessionId("alpha");

        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the screen-backed session in time.");

        var refreshResponse = await client.PostAsync("/sessions/alpha/ui/refresh", content: null);
        refreshResponse.EnsureSuccessStatusCode();

        var semantic = await client.GetFromJsonAsync<UiSemanticExtractionResultDto>("/sessions/alpha/semantic");
        var summary = await client.GetFromJsonAsync<SemanticSummaryDto>("/sessions/alpha/semantic/summary");

        Assert.NotNull(semantic);
        Assert.NotNull(summary);
        Assert.NotEmpty(semantic!.Packages);
        var package = Assert.Single(semantic.Packages);
        Assert.Equal(EveLikeSemanticPackage.SemanticPackageName, package.PackageName);
        Assert.NotNull(package.EveLike);
        Assert.NotNull(package.EveLike!.TravelRoute);
        Assert.NotNull(package.EveLike.ConfidenceSummary);
        Assert.True(package.EveLike.ConfidenceSummary.ContainsKey("route"));
        Assert.NotNull(package.EveLike.TravelRoute.Reasons);
        Assert.True(summary!.PackageCount >= 1);
    }

    private static SessionHostOptions CreateOptions() =>
        new()
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
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
                    RegionLayoutProfile = "DefaultDesktopGrid",
                    ProcessName = "ScreenApp",
                    WindowTitleFragment = "Screen Fixture",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitle,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = false,
                    Metadata =
                    {
                        ["UiSource"] = "ScreenCapture",
                        ["ObservabilityBackend"] = "ScreenCapture",
                        ["SemanticPackage"] = EveLikeSemanticPackage.SemanticPackageName,
                        ["EnableFramePreprocessing"] = true.ToString(),
                        ["FramePreprocessingProfile"] = "DefaultFramePreprocessing",
                        ["FramePreprocessingRegionSet"] = "window.right",
                        ["FramePreprocessingIncludeThreshold"] = true.ToString(),
                        ["EnableOcr"] = true.ToString(),
                        ["OcrProfile"] = "DefaultRegionOcrWithFullFrameFallback",
                        ["OcrRegionSet"] = "window.right",
                        ["OcrPreferredArtifactKinds"] = "threshold,high-contrast,grayscale,raw",
                        ["OcrIncludeFullFrameFallback"] = false.ToString()
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

    private sealed class RouteOcrEngine : IOcrEngine
    {
        public string EngineName => nameof(RouteOcrEngine);

        public string BackendName => "deterministic";

        public ValueTask<OcrEngineResult> ExtractAsync(ProcessedFrameArtifact artifact, CancellationToken cancellationToken)
        {
            if (!string.Equals(artifact.SourceRegionName, "window.right", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(new OcrEngineResult([], [], null, [], [], new Dictionary<string, string?>()));
            }

            var lines = new[]
            {
                "Route",
                "Destination: Jita",
                "Current location: Perimeter",
                "Next waypoint: New Caldari",
                "Sobaseki"
            };

            return ValueTask.FromResult(new OcrEngineResult(
                lines.Select(line => new OcrEngineTextFragment(line, line, 0.92d, null)).ToArray(),
                lines.Select((line, index) => new OcrEngineTextLine(line, line, 0.92d, new UiBounds(0, index * 16, artifact.OutputWidth, 14))).ToArray(),
                0.92d,
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
            return ValueTask.FromResult(new OcrEngineResult([], [], null, [], [], metadata));
        }
    }

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

    private static byte[] CreateTestPng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 11) % 255, (y * 17) % 255, 120));
            }
        }

        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }
}
