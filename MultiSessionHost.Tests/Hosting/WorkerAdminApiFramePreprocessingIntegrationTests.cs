using System.Net.Http.Json;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiFramePreprocessingIntegrationTests
{
    [Fact]
    public async Task PreprocessingEndpoints_ReturnFullPayloadAndLightweightSummaries()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var process = new DesktopProcessInfo(321, "ScreenApp", null, 456);
        var window = new DesktopWindowInfo(456, 321, "Screen Fixture", true);
        var capture = new WindowFrameCaptureResult(
            new UiBounds(50, 60, 800, 600),
            800,
            600,
            "image/png",
            "Format32bppArgb",
            CreateTestPng(32, 24),
            "FakeCapture");

        await using var harness = await WorkerHostHarness.StartAsync(
            CreateOptions(),
            services =>
            {
                services.AddSingleton<IClock>(clock);
                services.AddSingleton<IProcessLocator>(new StubProcessLocator(process));
                services.AddSingleton<IWindowLocator>(new StubWindowLocator(window));
                services.AddSingleton<IWindowFrameCapture>(new StubWindowFrameCapture(capture));
            });

        var client = Assert.IsType<HttpClient>(harness.Client);
        var sessionId = new SessionId("alpha");

        await TestWait.UntilAsync(
            () => harness.Coordinator.GetSession(sessionId)?.Runtime.CurrentStatus == SessionStatus.Running,
            TimeSpan.FromSeconds(10),
            "The worker runtime did not start the screen-backed session in time.");

        var refreshResponse = await client.PostAsync("/sessions/alpha/ui/refresh", content: null);
        refreshResponse.EnsureSuccessStatusCode();

        var full = await client.GetFromJsonAsync<SessionFramePreprocessingResultDto>("/sessions/alpha/preprocessing");
        var summary = await client.GetFromJsonAsync<SessionFramePreprocessingSummaryDto>("/sessions/alpha/preprocessing/summary");
        var allFull = await client.GetFromJsonAsync<SessionFramePreprocessingResultDto[]>("/preprocessing");
        var allSummaries = await client.GetFromJsonAsync<SessionFramePreprocessingSummaryDto[]>("/preprocessing/summaries");

        Assert.NotNull(full);
        Assert.NotNull(summary);
        Assert.NotNull(allFull);
        Assert.NotNull(allSummaries);
        Assert.Equal("alpha", full!.SessionId);
        Assert.Equal("ScreenCaptureDesktop", full.TargetKind);
        Assert.True(full.TotalArtifactCount >= 3);
        Assert.Contains(full.Artifacts, static artifact => artifact.ArtifactName == "frame.raw" && artifact.ImageBytes.Length > 0);
        Assert.Contains(full.Artifacts, static artifact => artifact.ArtifactName == "frame.grayscale" && artifact.ImageBytes.Length > 0);
        Assert.Equal("alpha", summary!.SessionId);
        Assert.Equal(full.SourceSnapshotSequence, summary.SourceSnapshotSequence);
        Assert.Equal(full.TotalArtifactCount, summary.TotalArtifactCount);
        Assert.Null(typeof(ProcessedFrameArtifactSummaryDto).GetProperty("ImageBytes"));
        Assert.Single(allFull!);
        Assert.Single(allSummaries!);
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
                        ["EnableFramePreprocessing"] = true.ToString(),
                        ["FramePreprocessingProfile"] = "DefaultFramePreprocessing",
                        ["FramePreprocessingRegionSet"] = "window.top,window.center,window.left,window.right"
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
                bitmap.SetPixel(
                    x,
                    y,
                    Color.FromArgb(
                        255,
                        (x * 11 + y * 5) % 255,
                        (x * 7 + y * 13) % 255,
                        (x * 3 + y * 17) % 255));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
