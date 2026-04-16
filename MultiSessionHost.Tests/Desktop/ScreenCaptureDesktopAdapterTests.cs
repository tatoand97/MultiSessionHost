using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class ScreenCaptureDesktopAdapterTests
{
    [Fact]
    public async Task Adapter_CapturesScreenSnapshotWithMetadata()
    {
        var fixture = CreateFixture();
        var capture = new WindowFrameCaptureResult(
            new UiBounds(10, 20, 640, 480),
            640,
            480,
            "image/png",
            "Format32bppArgb",
            [1, 2, 3, 4],
            "FakeCapture");
        var adapter = new ScreenCaptureDesktopTargetAdapter(
            new StubWindowFrameCapture(capture),
            fixture.ProcessLocator,
            fixture.WindowLocator,
            new NoOpObservabilityRecorder());

        var envelope = await adapter.CaptureUiSnapshotAsync(fixture.Snapshot, fixture.Context, fixture.Attachment, CancellationToken.None);

        Assert.Equal(DesktopTargetKind.ScreenCaptureDesktop.ToString(), envelope.Metadata["targetKind"]);
        Assert.Equal("ScreenCaptureDesktopTargetAdapter", envelope.Metadata["adapter"]);
        Assert.Equal("ScreenCapture", envelope.Metadata["captureSource"]);
        Assert.Equal("FakeCapture", envelope.Metadata["captureBackend"]);
        Assert.Equal(640, envelope.Root.GetProperty("imageWidth").GetInt32());
        Assert.Equal(480, envelope.Root.GetProperty("imageHeight").GetInt32());
        Assert.Equal("image/png", envelope.Root.GetProperty("imageFormat").GetString());
        Assert.Equal("ScreenCaptureDesktop", envelope.Root.GetProperty("metadata").GetProperty("targetKind").GetString());
        Assert.Equal(4, envelope.Root.GetProperty("imageBytes").GetBytesFromBase64().Length);
    }

    [Fact]
    public async Task Adapter_ValidationDetectsMissingWindow()
    {
        var fixture = CreateFixture(windowAvailable: false);
        var adapter = new ScreenCaptureDesktopTargetAdapter(
            new StubWindowFrameCapture(new WindowFrameCaptureResult(new UiBounds(0, 0, 1, 1), 1, 1, "image/png", "Format32bppArgb", [1], "FakeCapture")),
            fixture.ProcessLocator,
            fixture.WindowLocator,
            new NoOpObservabilityRecorder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.ValidateAttachmentAsync(fixture.Snapshot, fixture.Context, fixture.Attachment, CancellationToken.None));

        Assert.Contains("window", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Fixture CreateFixture(bool processAvailable = true, bool windowAvailable = true)
    {
        var sessionId = new SessionId("alpha");
        var definition = TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")).ToSessionDefinitions().Single();
        var snapshot = new SessionSnapshot(
            definition,
            SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with { DesiredStatus = SessionStatus.Running },
            PendingWorkItems: 0);
        var metadata = new Dictionary<string, string?> { ["UiSource"] = "ScreenCapture" };
        var target = new DesktopSessionTarget(
            sessionId,
            "screen-profile",
            DesktopTargetKind.ScreenCaptureDesktop,
            DesktopSessionMatchingMode.WindowTitle,
            "NativeApp",
            "Native Fixture",
            null,
            null,
            metadata);
        var profile = new DesktopTargetProfile(
            "screen-profile",
            DesktopTargetKind.ScreenCaptureDesktop,
            "NativeApp",
            "Native Fixture",
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            metadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: false);
        var context = new ResolvedDesktopTargetContext(
            sessionId,
            profile,
            new SessionTargetBinding(sessionId, "screen-profile", new Dictionary<string, string>(), null),
            target,
            new Dictionary<string, string> { ["SessionId"] = sessionId.Value });
        var process = new DesktopProcessInfo(100, "NativeApp", null, 200);
        var window = new DesktopWindowInfo(200, 100, "Native Fixture", true);
        var attachment = new DesktopSessionAttachment(sessionId, target, process, window, null, DateTimeOffset.UtcNow);

        return new Fixture(
            snapshot,
            context,
            attachment,
            new StubProcessLocator(processAvailable ? process : null),
            new StubWindowLocator(windowAvailable ? window : null));
    }

    private sealed record Fixture(
        SessionSnapshot Snapshot,
        ResolvedDesktopTargetContext Context,
        DesktopSessionAttachment Attachment,
        StubProcessLocator ProcessLocator,
        StubWindowLocator WindowLocator);

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
