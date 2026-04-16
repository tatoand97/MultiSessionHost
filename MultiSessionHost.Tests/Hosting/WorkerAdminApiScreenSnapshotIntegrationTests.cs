using System.Net.Http.Json;
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

public sealed class WorkerAdminApiScreenSnapshotIntegrationTests
{
    [Fact]
    public async Task ScreenEndpoints_ReturnFullPayloadSummariesAndHistory()
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
            [10, 20, 30],
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

        var full = await client.GetFromJsonAsync<SessionScreenSnapshotDto>("/sessions/alpha/screen");
        var summary = await client.GetFromJsonAsync<SessionScreenSnapshotSummaryDto>("/sessions/alpha/screen/summary");
        var history = await client.GetFromJsonAsync<SessionScreenSnapshotHistoryDto>("/sessions/alpha/screen/history");
        var allFull = await client.GetFromJsonAsync<SessionScreenSnapshotDto[]>("/screen");
        var allSummaries = await client.GetFromJsonAsync<SessionScreenSnapshotSummaryDto[]>("/screen/summaries");

        Assert.NotNull(full);
        Assert.NotNull(summary);
        Assert.NotNull(history);
        Assert.NotNull(allFull);
        Assert.NotNull(allSummaries);
        Assert.Equal("alpha", full!.SessionId);
        Assert.Equal(3, full.ImageBytes.Length);
        Assert.Equal(3, full.PayloadByteLength);
        Assert.Equal("ScreenCaptureDesktop", full.TargetKind);
        Assert.Equal("ScreenCapture", full.CaptureSource);
        Assert.Equal("FakeCapture", full.CaptureBackend);
        Assert.Equal(800, full.ImageWidth);
        Assert.Equal(600, full.ImageHeight);
        Assert.Equal("alpha", summary!.SessionId);
        Assert.Equal(3, summary.PayloadByteLength);
        Assert.Equal("ScreenCapture", summary.ObservabilityBackend);
        Assert.Equal(800, summary.ImageWidth);
        Assert.Null(typeof(SessionScreenSnapshotSummaryDto).GetProperty("ImageBytes"));
        Assert.Single(history!.Entries);
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
                    ProcessName = "ScreenApp",
                    WindowTitleFragment = "Screen Fixture",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitle,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = false,
                    Metadata =
                    {
                        ["UiSource"] = "ScreenCapture",
                        ["ObservabilityBackend"] = "ScreenCapture"
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
}
