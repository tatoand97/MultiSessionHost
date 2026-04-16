using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
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

        var state = await refreshService.RefreshAsync(snapshot, context, attachment, CancellationToken.None);

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
