using System.Text.Json;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class WindowsUiAutomationDesktopAdapterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NativeAdapter_CapturesUiSnapshotWithoutHttpBaseAddress()
    {
        var fixture = CreateFixture();
        var root = CreateNativeRoot("Start", "Launch");
        var reader = new StubNativeUiAutomationReader(root);
        var adapter = new WindowsUiAutomationDesktopTargetAdapter(reader, fixture.ProcessLocator, fixture.WindowLocator, new NoOpObservabilityRecorder());

        var envelope = await adapter.CaptureUiSnapshotAsync(fixture.Snapshot, fixture.Context, fixture.Attachment, CancellationToken.None);

        Assert.Equal(DesktopTargetKind.WindowsUiAutomationDesktop.ToString(), envelope.Metadata["targetKind"]);
        Assert.Equal("WindowsUiAutomationDesktopTargetAdapter", envelope.Metadata["adapter"]);
        Assert.Null(fixture.Attachment.BaseAddress);
        Assert.Equal("WindowsUiAutomation", envelope.Root.GetProperty("metadata").GetProperty("source").GetString());
        Assert.Equal("Button", envelope.Root.GetProperty("children")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task NativeAdapter_ValidationDetectsMissingWindow()
    {
        var fixture = CreateFixture(windowAvailable: false);
        var adapter = new WindowsUiAutomationDesktopTargetAdapter(new StubNativeUiAutomationReader(CreateNativeRoot("Start", "Launch")), fixture.ProcessLocator, fixture.WindowLocator, new NoOpObservabilityRecorder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.ValidateAttachmentAsync(fixture.Snapshot, fixture.Context, fixture.Attachment, CancellationToken.None));

        Assert.Contains("window", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeNormalizer_MapsAutomationFieldsIntoGenericUiNodes()
    {
        var root = CreateNativeRoot("Start", "Launch");
        var element = JsonSerializer.SerializeToElement(root, JsonOptions);
        var metadata = new UiSnapshotMetadata("alpha", "native", DateTimeOffset.UtcNow, 100, 200, "Window", new Dictionary<string, string?>());
        var tree = new WindowsUiAutomationUiTreeNormalizer().Normalize(metadata, element);

        var button = tree.Root.Children.Single();

        Assert.Equal("Window", tree.Root.Role);
        Assert.Equal("Button", button.Role);
        Assert.Equal("Start", button.Name);
        Assert.Equal("Launch", button.Text);
        Assert.True(button.Enabled);
        Assert.True(button.Visible);
        Assert.Contains(button.Attributes, attribute => attribute.Name == "automationId" && attribute.Value == "startButton");
        Assert.Contains(button.Attributes, attribute => attribute.Name == "identityQuality" && attribute.Value == "Strong");
    }

    [Fact]
    public void NativeIdentityBuilder_KeepsStrongIdsStableAcrossModestSiblingChurn()
    {
        var builder = new NativeUiAutomationIdentityBuilder();
        var before = builder.AssignIdentities(new NativeUiAutomationElementSnapshot(
            "Window",
            "Root",
            "root",
            null,
            "Win32",
            "WindowClass",
            true,
            false,
            false,
            null,
            null,
            null,
            new Dictionary<string, string?>(),
            [
                Element("Button", "Start", "startButton"),
                Element("Button", "Stop", "stopButton")
            ]));
        var after = builder.AssignIdentities(new NativeUiAutomationElementSnapshot(
            "Window",
            "Root",
            "root",
            null,
            "Win32",
            "WindowClass",
            true,
            false,
            false,
            null,
            null,
            null,
            new Dictionary<string, string?>(),
            [
                Element("Text", "Inserted", "insertedText"),
                Element("Button", "Start", "startButton"),
                Element("Button", "Stop", "stopButton")
            ]));

        Assert.Equal(before.Children[0].NodeId, after.Children[1].NodeId);
        Assert.Equal(before.Children[1].NodeId, after.Children[2].NodeId);
    }

    [Fact]
    public void NativeIdentityBuilder_UsesFallbackWhenNoStrongOrSemanticIdentityExists()
    {
        var builder = new NativeUiAutomationIdentityBuilder();
        var root = builder.AssignIdentities(new NativeUiAutomationElementSnapshot(
            "Pane",
            null,
            null,
            null,
            null,
            null,
            true,
            false,
            false,
            null,
            null,
            null,
            new Dictionary<string, string?>(),
            []));

        Assert.Equal("Fallback", root.IdentityQuality);
        Assert.Equal("ancestor-path+role+occurrence", root.IdentityBasis);
        Assert.StartsWith("uia:", root.NodeId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeAdapter_RecordsNativeCaptureFailureThroughObservability()
    {
        var fixture = CreateFixture();
        var recorder = new RecordingObservabilityRecorder();
        var adapter = new WindowsUiAutomationDesktopTargetAdapter(new ThrowingNativeUiAutomationReader(), fixture.ProcessLocator, fixture.WindowLocator, recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.CaptureUiSnapshotAsync(fixture.Snapshot, fixture.Context, fixture.Attachment, CancellationToken.None));

        Assert.Contains(recorder.ActivityStages, stage => stage == "native.capture.failed");
        Assert.Contains(recorder.AdapterErrors, operation => operation == "native.capture");
    }

    private static NativeUiAutomationNode CreateNativeRoot(string buttonName, string buttonValue)
    {
        var builder = new NativeUiAutomationIdentityBuilder();
        return builder.AssignIdentities(new NativeUiAutomationElementSnapshot(
            "Window",
            "Native Fixture",
            "rootWindow",
            null,
            "Win32",
            "FixtureWindow",
            true,
            false,
            false,
            null,
            null,
            new UiBounds(0, 0, 640, 480),
            new Dictionary<string, string?> { ["source"] = "WindowsUiAutomation" },
            [Element("Button", buttonName, "startButton", buttonValue)]));
    }

    private static NativeUiAutomationElementSnapshot Element(string role, string? name, string? automationId, string? value = null) =>
        new(
            role,
            name,
            automationId,
            null,
            "Win32",
            $"{role}Class",
            true,
            false,
            false,
            null,
            value,
            new UiBounds(10, 10, 100, 24),
            new Dictionary<string, string?>(),
            []);

    private static Fixture CreateFixture(bool processAvailable = true, bool windowAvailable = true)
    {
        var sessionId = new SessionId("alpha");
        var options = TestOptionsFactory.Create(TestOptionsFactory.Session("alpha"));
        var definition = options.ToSessionDefinitions().Single();
        var snapshot = new SessionSnapshot(
            definition,
            SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with { DesiredStatus = SessionStatus.Running },
            PendingWorkItems: 0);
        var target = new DesktopSessionTarget(
            sessionId,
            "native-profile",
            DesktopTargetKind.WindowsUiAutomationDesktop,
            DesktopSessionMatchingMode.WindowTitle,
            "NativeApp",
            "Native Fixture",
            null,
            null,
            new Dictionary<string, string?> { ["NativeUiAutomation.MaxDepth"] = "4" });
        var profile = new DesktopTargetProfile(
            "native-profile",
            DesktopTargetKind.WindowsUiAutomationDesktop,
            "NativeApp",
            "Native Fixture",
            null,
            null,
            DesktopSessionMatchingMode.WindowTitle,
            target.Metadata,
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: false);
        var context = new ResolvedDesktopTargetContext(
            sessionId,
            profile,
            new SessionTargetBinding(sessionId, "native-profile", new Dictionary<string, string>(), null),
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

    private sealed class StubNativeUiAutomationReader : INativeUiAutomationReader
    {
        private readonly NativeUiAutomationNode _root;

        public StubNativeUiAutomationReader(NativeUiAutomationNode root)
        {
            _root = root;
        }

        public Task<NativeUiAutomationRawSnapshot> CaptureAsync(DesktopSessionAttachment attachment, NativeUiAutomationCaptureOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(new NativeUiAutomationRawSnapshot(
                _root,
                2,
                1,
                false,
                new Dictionary<string, string?> { ["captureSource"] = "WindowsUiAutomation" }));
    }

    private sealed class ThrowingNativeUiAutomationReader : INativeUiAutomationReader
    {
        public Task<NativeUiAutomationRawSnapshot> CaptureAsync(DesktopSessionAttachment attachment, NativeUiAutomationCaptureOptions options, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("native capture failed");
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

    private sealed class RecordingObservabilityRecorder : NoOpObservabilityRecorder
    {
        public List<string> ActivityStages { get; } = [];

        public List<string> AdapterErrors { get; } = [];

        public override ValueTask RecordActivityAsync(SessionId sessionId, string stage, string outcome, TimeSpan duration, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
        {
            ActivityStages.Add(stage);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RecordAdapterErrorAsync(SessionId sessionId, string adapterName, string operation, Exception exception, string? reasonCode, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
        {
            AdapterErrors.Add(operation);
            return ValueTask.CompletedTask;
        }
    }
}
