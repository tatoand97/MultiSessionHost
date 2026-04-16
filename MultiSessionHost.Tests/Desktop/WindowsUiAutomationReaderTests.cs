using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class WindowsUiAutomationReaderTests
{
    [Fact]
    public async Task CaptureAsync_UsesControlViewWithoutFallbackWhenControlChildrenExist()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow");
        root.AddControlChild(CreateElement("Button", name: "Start", automationId: "start"));
        root.AddRawChild(root.ControlChildren[0]);
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal("Control", snapshot.Metadata["requestedTreeView"]);
        Assert.Equal("Control", snapshot.Metadata["effectiveTreeView"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["fallbackApplied"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["controlViewHasFirstChild"]);
        Assert.Equal(2, snapshot.NodeCount);
        Assert.Single(snapshot.Root.Children);
    }

    [Fact]
    public async Task CaptureAsync_FallsBackToRawViewWhenControlHasNoChildrenAndRawDoes()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "trinityWindow");
        root.AddRawChild(CreateElement("Pane", name: "Inventory", frameworkId: "CustomNative"));
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(
            CreateAttachment(),
            CreateOptions(allowedFrameworkIds: ToSet("Win32")),
            CancellationToken.None);

        Assert.Equal("Control", snapshot.Metadata["requestedTreeView"]);
        Assert.Equal("Raw", snapshot.Metadata["effectiveTreeView"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["fallbackApplied"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["controlViewHasFirstChild"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["rawViewHasFirstChild"]);
        Assert.Equal("trinityWindow", snapshot.Metadata["rootClassName"]);
        Assert.Equal("Win32", snapshot.Metadata["rootFrameworkId"]);
        Assert.Single(snapshot.Root.Children);
        Assert.Equal("Pane", snapshot.Root.Children[0].Role);
    }

    [Fact]
    public async Task CaptureAsync_ExplainsRootOnlyCaptureWhenBothViewsHaveNoChildren()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow");
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal(1, snapshot.NodeCount);
        Assert.Empty(snapshot.Root.Children);
        Assert.Equal(bool.FalseString, snapshot.Metadata["fallbackApplied"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["controlViewHasFirstChild"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["rawViewHasFirstChild"]);
        Assert.Equal("0", snapshot.Metadata["rootScannedChildCount"]);
        Assert.Equal("0", snapshot.Metadata["rootIncludedChildCount"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["pointProbeEnabled"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["pointProbeReturnedOnlyRoot"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["opaqueRoot"]);
        Assert.Equal("RootOnly", snapshot.Metadata["observabilityMode"]);
        Assert.Equal("native.uia.root_only", snapshot.Metadata["targetOpacityReasonCode"]);
        Assert.Null(snapshot.Metadata["truncationReason"]);
    }

    [Fact]
    public async Task CaptureAsync_UsesPointProbeWhenTreeViewsExposeNoChildren()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow");
        var descendant = CreateElement("Pane", name: "Overlay", frameworkId: "CustomNative", className: "OverlayClass");
        descendant.SetParent(root);
        var reader = CreateReader(root, new Dictionary<string, FakeElement>(StringComparer.Ordinal)
        {
            ["50:10"] = descendant
        });

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal(bool.FalseString, snapshot.Metadata["controlViewHasFirstChild"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["rawViewHasFirstChild"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["pointProbeEnabled"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["pointProbeFoundDescendant"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["pointProbeReturnedOnlyRoot"]);
        Assert.Equal("2", snapshot.Metadata["pointProbeDistinctElementCount"]);
        Assert.Equal("1", snapshot.Metadata["pointProbeValidatedDistinctElementCount"]);
        Assert.Equal("0", snapshot.Metadata["pointProbeRejectedExternalCount"]);
        Assert.Equal("PointProbeOnly", snapshot.Metadata["observabilityMode"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["opaqueRoot"]);
        Assert.Null(snapshot.Metadata["targetOpacityReasonCode"]);
    }

    [Fact]
    public async Task CaptureAsync_PointProbeRejectsDescendantFromDifferentProcess()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow", processId: 4242, nativeWindowHandle: 1001);
        var external = CreateElement("Pane", name: "External", frameworkId: "Gecko", className: "MozillaWindowClass", processId: 9000, nativeWindowHandle: 8001);
        external.SetParent(root);
        var reader = CreateReader(root, new Dictionary<string, FakeElement>(StringComparer.Ordinal)
        {
            ["50:10"] = external
        });

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal(bool.FalseString, snapshot.Metadata["pointProbeFoundDescendant"]);
        Assert.Equal("2", snapshot.Metadata["pointProbeDistinctElementCount"]);
        Assert.Equal("0", snapshot.Metadata["pointProbeValidatedDistinctElementCount"]);
        Assert.Equal("1", snapshot.Metadata["pointProbeRejectedExternalCount"]);
        Assert.Equal("SameIdentityAsRoot:1,DifferentProcess:1", snapshot.Metadata["pointProbeRejectedReasonSummary"]);
        Assert.Equal("RootOnly", snapshot.Metadata["observabilityMode"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["opaqueRoot"]);
        Assert.Equal("native.uia.root_only", snapshot.Metadata["targetOpacityReasonCode"]);
    }

    [Fact]
    public async Task CaptureAsync_PointProbeRejectsSameProcessElementWithDifferentRootAncestry()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow", processId: 4242, nativeWindowHandle: 1001);
        var otherRoot = CreateElement("Window", frameworkId: "Win32", className: "OtherRoot", processId: 4242, nativeWindowHandle: 1009);
        var siblingWindowElement = CreateElement("Pane", name: "OtherOverlay", frameworkId: "Win32", className: "OtherOverlayClass", processId: 4242, nativeWindowHandle: 1009);
        siblingWindowElement.SetParent(otherRoot);
        var reader = CreateReader(root, new Dictionary<string, FakeElement>(StringComparer.Ordinal)
        {
            ["50:10"] = siblingWindowElement
        });

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal(bool.FalseString, snapshot.Metadata["pointProbeFoundDescendant"]);
        Assert.Equal("0", snapshot.Metadata["pointProbeValidatedDistinctElementCount"]);
        Assert.Equal("1", snapshot.Metadata["pointProbeRejectedExternalCount"]);
        Assert.Equal("SameIdentityAsRoot:1,DifferentRootAncestry:1", snapshot.Metadata["pointProbeRejectedReasonSummary"]);
        Assert.Equal("RootOnly", snapshot.Metadata["observabilityMode"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["opaqueRoot"]);
    }

    [Fact]
    public async Task CaptureAsync_PointProbeAcceptsSameProcessElementWithSameRootAncestry()
    {
        var root = CreateElement("Window", frameworkId: "Win32", className: "RootWindow", processId: 4242, nativeWindowHandle: 1001);
        var descendant = CreateElement("Pane", name: "Overlay", frameworkId: "CustomNative", className: "OverlayClass", processId: 4242, nativeWindowHandle: 1001);
        descendant.SetParent(root);
        var reader = CreateReader(root, new Dictionary<string, FakeElement>(StringComparer.Ordinal)
        {
            ["50:10"] = descendant
        });

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(), CancellationToken.None);

        Assert.Equal(bool.TrueString, snapshot.Metadata["pointProbeFoundDescendant"]);
        Assert.Equal("1", snapshot.Metadata["pointProbeValidatedDistinctElementCount"]);
        Assert.Equal("0", snapshot.Metadata["pointProbeRejectedExternalCount"]);
        Assert.Equal("SameIdentityAsRoot:1", snapshot.Metadata["pointProbeRejectedReasonSummary"]);
        Assert.Equal("PointProbeOnly", snapshot.Metadata["observabilityMode"]);
        Assert.Equal(bool.FalseString, snapshot.Metadata["opaqueRoot"]);
    }

    [Fact]
    public async Task CaptureAsync_ReportsOffscreenFilteringDiagnostics()
    {
        var root = CreateElement("Window");
        root.AddControlChild(CreateElement("Button", name: "Hidden", isOffscreen: true));
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(includeOffscreenNodes: false), CancellationToken.None);

        Assert.Equal("1", snapshot.Metadata["rootScannedChildCount"]);
        Assert.Equal("0", snapshot.Metadata["rootIncludedChildCount"]);
        Assert.Equal("1", snapshot.Metadata["childrenFilteredByOffscreen"]);
        Assert.Empty(snapshot.Root.Children);
    }

    [Fact]
    public async Task CaptureAsync_FallbackIncludesOffscreenChildrenForDiagnosticCapture()
    {
        var root = CreateElement("Window");
        root.AddRawChild(CreateElement("Pane", name: "HiddenRawPane", isOffscreen: true, frameworkId: "CustomNative"));
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(
            CreateAttachment(),
            CreateOptions(includeOffscreenNodes: false, allowedFrameworkIds: ToSet("Win32")),
            CancellationToken.None);

        Assert.Equal(bool.TrueString, snapshot.Metadata["fallbackApplied"]);
        Assert.Equal(bool.TrueString, snapshot.Metadata["includeOffscreenNodes"]);
        Assert.Single(snapshot.Root.Children);
        Assert.True(snapshot.Root.Children[0].IsOffscreen);
    }

    [Fact]
    public async Task CaptureAsync_ReportsFrameworkFilteringDiagnostics()
    {
        var root = CreateElement("Window");
        root.AddControlChild(CreateElement("Pane", name: "Filtered", frameworkId: "CustomNative"));
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(
            CreateAttachment(),
            CreateOptions(allowedFrameworkIds: ToSet("Win32")),
            CancellationToken.None);

        Assert.Equal("1", snapshot.Metadata["childrenFilteredByFramework"]);
        Assert.Equal("0", snapshot.Metadata["rootIncludedChildCount"]);
        Assert.Empty(snapshot.Root.Children);
    }

    [Fact]
    public async Task CaptureAsync_ReportsDepthTruncationSeparately()
    {
        var root = CreateElement("Window");
        var child = CreateElement("Pane", name: "Level1");
        child.AddControlChild(CreateElement("Button", name: "Level2"));
        root.AddControlChild(child);
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(maxDepth: 1), CancellationToken.None);

        Assert.True(snapshot.Truncated);
        Assert.Equal("MaxDepth", snapshot.Metadata["truncationReason"]);
        Assert.Single(snapshot.Root.Children);
        Assert.Empty(snapshot.Root.Children[0].Children);
    }

    [Fact]
    public async Task CaptureAsync_ReportsChildCountTruncationSeparately()
    {
        var root = CreateElement("Window");
        root.AddControlChild(CreateElement("Button", name: "One"));
        root.AddControlChild(CreateElement("Button", name: "Two"));
        var reader = CreateReader(root);

        var snapshot = await reader.CaptureAsync(CreateAttachment(), CreateOptions(maxChildrenPerNode: 1), CancellationToken.None);

        Assert.True(snapshot.Truncated);
        Assert.Equal("MaxChildrenPerNode", snapshot.Metadata["truncationReason"]);
        Assert.Single(snapshot.Root.Children);
    }

    private static WindowsUiAutomationReader CreateReader(FakeElement root, IReadOnlyDictionary<string, FakeElement>? pointProbeMap = null) =>
        new(new NativeUiAutomationIdentityBuilder(), new FakePlatform(root, pointProbeMap));

    private static NativeUiAutomationCaptureOptions CreateOptions(
        int maxDepth = 8,
        int maxChildrenPerNode = 200,
        bool includeOffscreenNodes = false,
        string treeView = "Control",
        IReadOnlySet<string>? allowedFrameworkIds = null,
        bool preserveFrameworkFilterOnDiagnosticFallback = false,
        bool enablePointProbe = true,
        int pointProbeInsetPixels = 24,
        bool enablePointProbeGrid = true) =>
        new(
            maxDepth,
            maxChildrenPerNode,
            includeOffscreenNodes,
            treeView,
            allowedFrameworkIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            preserveFrameworkFilterOnDiagnosticFallback,
            enablePointProbe,
            pointProbeInsetPixels,
            enablePointProbeGrid);

    private static IReadOnlySet<string> ToSet(params string[] values) =>
        values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static DesktopSessionAttachment CreateAttachment()
    {
        var sessionId = new SessionId("alpha");
        var target = new DesktopSessionTarget(
            sessionId,
            "native-profile",
            DesktopTargetKind.WindowsUiAutomationDesktop,
            DesktopSessionMatchingMode.WindowTitle,
            "exefile",
            "EVE - Tatoand",
            null,
            null,
            new Dictionary<string, string?>());
        var process = new DesktopProcessInfo(4242, "exefile", null, 1001);
        var window = new DesktopWindowInfo(1001, 4242, "EVE - Tatoand", true);
        return new DesktopSessionAttachment(sessionId, target, process, window, null, DateTimeOffset.UtcNow);
    }

    private static FakeElement CreateElement(
        string role,
        string? name = null,
        string? automationId = null,
        string? frameworkId = "Win32",
        string? className = "NativeClass",
        bool isOffscreen = false,
        int processId = 4242,
        int nativeWindowHandle = 1001) =>
        new(role, name, automationId, frameworkId, className, isOffscreen, processId, nativeWindowHandle);

    private sealed class FakePlatform : IWindowsUiAutomationReaderPlatform
    {
        private readonly FakeElement _root;
        private readonly IReadOnlyDictionary<string, FakeElement> _pointProbeMap;

        public FakePlatform(FakeElement root, IReadOnlyDictionary<string, FakeElement>? pointProbeMap)
        {
            _root = root;
            _pointProbeMap = pointProbeMap ?? new Dictionary<string, FakeElement>(StringComparer.Ordinal);
        }

        public IWindowsUiAutomationElement GetRoot(long windowHandle) => _root;

        public IWindowsUiAutomationElement? GetFirstChild(IWindowsUiAutomationElement element, string treeView) =>
            GetChildren((FakeElement)element, treeView).FirstOrDefault();

        public IWindowsUiAutomationElement? GetNextSibling(IWindowsUiAutomationElement element, string treeView)
        {
            var fake = (FakeElement)element;

            if (fake.Parent is null)
            {
                return null;
            }

            var siblings = GetChildren(fake.Parent, treeView);
            var index = siblings
                .Select((candidate, position) => new { candidate, position })
                .FirstOrDefault(entry => ReferenceEquals(entry.candidate, fake))
                ?.position ?? -1;
            return index >= 0 && index + 1 < siblings.Count
                ? siblings[index + 1]
                : null;
        }

        public IWindowsUiAutomationElement? GetElementFromPoint(double x, double y)
        {
            var key = $"{Math.Round(x):F0}:{Math.Round(y):F0}";
            return _pointProbeMap.TryGetValue(key, out var element)
                ? element
                : _root;
        }

        public IWindowsUiAutomationElement? GetParent(IWindowsUiAutomationElement element, string? treeView = null) =>
            ((FakeElement)element).Parent;

        private static IReadOnlyList<FakeElement> GetChildren(FakeElement element, string treeView) =>
            string.Equals(treeView, "Raw", StringComparison.OrdinalIgnoreCase)
                ? element.RawChildren
                : element.ControlChildren;
    }

    private sealed class FakeElement : IWindowsUiAutomationElement
    {
        public FakeElement(
            string role,
            string? name,
            string? automationId,
            string? frameworkId,
            string? className,
            bool isOffscreen,
            int processId,
            int nativeWindowHandle)
        {
            Role = role;
            Name = name;
            AutomationId = automationId;
            FrameworkId = frameworkId;
            ClassName = className;
            IsOffscreen = isOffscreen;
            RuntimeId = Guid.NewGuid().ToString("N");
            LocalizedControlType = role.ToLowerInvariant();
            NativeWindowHandle = nativeWindowHandle;
            ProcessId = processId;
        }

        public List<FakeElement> ControlChildren { get; } = [];

        public List<FakeElement> RawChildren { get; } = [];

        public FakeElement? Parent { get; private set; }

        public string Role { get; }

        public string? Name { get; }

        public string? AutomationId { get; }

        public string? RuntimeId { get; }

        public string? FrameworkId { get; }

        public string? ClassName { get; }

        public bool IsEnabled => true;

        public bool IsOffscreen { get; }

        public bool HasKeyboardFocus => false;

        public bool? IsSelected => null;

        public string? Value => null;

        public UiBounds? Bounds => new(0, 0, 100, 20);

        public string? LocalizedControlType { get; }

        public int? NativeWindowHandle { get; }

        public int? ProcessId { get; }

        public bool IsPassword => false;

        public void AddControlChild(FakeElement child)
        {
            child.Parent = this;
            ControlChildren.Add(child);
        }

        public void AddRawChild(FakeElement child)
        {
            child.Parent = this;
            RawChildren.Add(child);
        }

        public void SetParent(FakeElement parent)
        {
            Parent = parent;
        }
    }
}
