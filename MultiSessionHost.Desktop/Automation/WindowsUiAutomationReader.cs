using System.Globalization;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

public sealed class WindowsUiAutomationReader : INativeUiAutomationReader
{
    private static readonly IReadOnlySet<string> EmptyFrameworkFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly NativeUiAutomationIdentityBuilder _identityBuilder;
    private readonly IWindowsUiAutomationReaderPlatform _platform;

    public WindowsUiAutomationReader(NativeUiAutomationIdentityBuilder identityBuilder)
        : this(identityBuilder, new WindowsUiAutomationReaderPlatform())
    {
    }

    internal WindowsUiAutomationReader(
        NativeUiAutomationIdentityBuilder identityBuilder,
        IWindowsUiAutomationReaderPlatform platform)
    {
        _identityBuilder = identityBuilder;
        _platform = platform;
    }

    public Task<NativeUiAutomationRawSnapshot> CaptureAsync(
        DesktopSessionAttachment attachment,
        NativeUiAutomationCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows UI Automation capture is only supported on Windows.");
        }

        if (attachment.Window.WindowHandle == 0)
        {
            throw new InvalidOperationException($"Session '{attachment.SessionId}' does not have a window handle for native UI Automation capture.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var rootElement = _platform.GetRoot(attachment.Window.WindowHandle);
        var requestedTreeView = NormalizeTreeView(options.TreeView);
        var controlFirstChild = _platform.GetFirstChild(rootElement, "Control");
        var rawFirstChild = _platform.GetFirstChild(rootElement, "Raw");

        var fallbackApplied = requestedTreeView == "Control" &&
            controlFirstChild is null &&
            rawFirstChild is not null;
        var effectiveOptions = fallbackApplied
            ? CreateDiagnosticFallbackOptions(options)
            : options with { TreeView = requestedTreeView };
        var effectiveTreeView = NormalizeTreeView(effectiveOptions.TreeView);

        var pointProbe = RunPointProbe(rootElement, effectiveOptions, cancellationToken);
        var rootKey = BuildElementIdentityKey(rootElement);
        var pointProbeFoundDescendant = pointProbe.DistinctElements.Keys.Any(key => !string.Equals(key, rootKey, StringComparison.Ordinal));
        var pointProbeReturnedOnlyRoot = pointProbe.DistinctElements.Count > 0 && !pointProbeFoundDescendant;

        var stats = new CaptureStats();
        var rawRoot = CaptureElement(rootElement, effectiveOptions, depth: 0, isRoot: true, stats, cancellationToken);
        var root = _identityBuilder.AssignIdentities(rawRoot);

        var opaqueRoot = controlFirstChild is null &&
            rawFirstChild is null &&
            !pointProbeFoundDescendant;

        var observabilityMode = opaqueRoot
            ? "RootOnly"
            : controlFirstChild is null && rawFirstChild is null && pointProbeFoundDescendant
                ? "PointProbeOnly"
                : "ObservableTree";

        var opacityReasonCode = opaqueRoot ? "native.uia.root_only" : null;
        var opacityReason = opaqueRoot
            ? "UI Automation exposed only the native root window and no actionable descendants."
            : null;

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["captureSource"] = "WindowsUiAutomation",
            ["automationBackend"] = "System.Windows.Automation",
            ["treeView"] = effectiveTreeView,
            ["requestedTreeView"] = requestedTreeView,
            ["effectiveTreeView"] = effectiveTreeView,
            ["controlViewHasFirstChild"] = (controlFirstChild is not null).ToString(),
            ["rawViewHasFirstChild"] = (rawFirstChild is not null).ToString(),
            ["fallbackApplied"] = fallbackApplied.ToString(),
            ["fallbackPreservedFrameworkFilter"] = (fallbackApplied && options.PreserveFrameworkFilterOnDiagnosticFallback).ToString(),
            ["maxDepth"] = options.MaxDepth.ToString(CultureInfo.InvariantCulture),
            ["maxChildrenPerNode"] = options.MaxChildrenPerNode.ToString(CultureInfo.InvariantCulture),
            ["includeOffscreenNodes"] = effectiveOptions.IncludeOffscreenNodes.ToString(),
            ["rootRuntimeId"] = rootElement.RuntimeId,
            ["rootAutomationId"] = rootElement.AutomationId,
            ["rootFrameworkId"] = rootElement.FrameworkId,
            ["rootClassName"] = rootElement.ClassName,
            ["rootLocalizedControlType"] = rootElement.LocalizedControlType,
            ["rootNativeWindowHandle"] = rootElement.NativeWindowHandle?.ToString(CultureInfo.InvariantCulture),
            ["rootProcessId"] = rootElement.ProcessId?.ToString(CultureInfo.InvariantCulture),
            ["rootScannedChildCount"] = stats.RootScannedChildCount.ToString(CultureInfo.InvariantCulture),
            ["rootIncludedChildCount"] = stats.RootIncludedChildCount.ToString(CultureInfo.InvariantCulture),
            ["childrenFilteredByOffscreen"] = stats.ChildrenFilteredByOffscreen.ToString(CultureInfo.InvariantCulture),
            ["childrenFilteredByFramework"] = stats.ChildrenFilteredByFramework.ToString(CultureInfo.InvariantCulture),
            ["pointProbeEnabled"] = pointProbe.Enabled.ToString(),
            ["pointProbeCount"] = pointProbe.PointCount.ToString(CultureInfo.InvariantCulture),
            ["pointProbeDistinctElementCount"] = pointProbe.DistinctElements.Count.ToString(CultureInfo.InvariantCulture),
            ["pointProbeFoundDescendant"] = pointProbeFoundDescendant.ToString(),
            ["pointProbeReturnedOnlyRoot"] = pointProbeReturnedOnlyRoot.ToString(),
            ["pointProbeFrameworkIds"] = JoinDistinct(pointProbe.DistinctElements.Values.Select(static element => element.FrameworkId)),
            ["pointProbeClassNames"] = JoinDistinct(pointProbe.DistinctElements.Values.Select(static element => element.ClassName)),
            ["pointProbeControlTypes"] = JoinDistinct(pointProbe.DistinctElements.Values.Select(static element => element.Role)),
            ["observabilityMode"] = observabilityMode,
            ["opaqueRoot"] = opaqueRoot.ToString(),
            ["targetOpacityReasonCode"] = opacityReasonCode,
            ["targetOpacityReason"] = opacityReason,
            ["truncationReason"] = stats.GetTruncationReason()
        };

        return Task.FromResult(new NativeUiAutomationRawSnapshot(root, stats.NodeCount, stats.MaxDepth, stats.Truncated, metadata));
    }

    private PointProbeDiagnostics RunPointProbe(
        IWindowsUiAutomationElement rootElement,
        NativeUiAutomationCaptureOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnablePointProbe)
        {
            return PointProbeDiagnostics.Disabled;
        }

        var points = BuildProbePoints(rootElement.Bounds, options.PointProbeInsetPixels, options.EnablePointProbeGrid);
        if (points.Count == 0)
        {
            return new PointProbeDiagnostics(true, 0, new Dictionary<string, IWindowsUiAutomationElement>(StringComparer.Ordinal));
        }

        var distinctElements = new Dictionary<string, IWindowsUiAutomationElement>(StringComparer.Ordinal);

        foreach (var point in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var element = _platform.GetElementFromPoint(point.X, point.Y);
            if (element is null)
            {
                continue;
            }

            var identityKey = BuildElementIdentityKey(element);
            if (!distinctElements.ContainsKey(identityKey))
            {
                distinctElements[identityKey] = element;
            }
        }

        return new PointProbeDiagnostics(true, points.Count, distinctElements);
    }

    private static List<ProbePoint> BuildProbePoints(UiBounds? bounds, int insetPixels, bool includeGrid)
    {
        if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return [];
        }

        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;

        var insetX = Math.Min(Math.Max(1, insetPixels), Math.Max(1, bounds.Width / 3));
        var insetY = Math.Min(Math.Max(1, insetPixels), Math.Max(1, bounds.Height / 3));

        var xLeft = left + insetX;
        var xRight = Math.Max(xLeft, right - insetX);
        var yTop = top + insetY;
        var yBottom = Math.Max(yTop, bottom - insetY);
        var xCenter = left + (bounds.Width / 2d);
        var yCenter = top + (bounds.Height / 2d);

        var points = new List<ProbePoint>
        {
            new(xCenter, yCenter),
            new(xLeft, yTop),
            new(xRight, yTop),
            new(xLeft, yBottom),
            new(xRight, yBottom)
        };

        if (includeGrid)
        {
            var xGrid = new[] { 0.2d, 0.5d, 0.8d }.Select(fraction => left + (bounds.Width * fraction)).ToArray();
            var yGrid = new[] { 0.2d, 0.5d, 0.8d }.Select(fraction => top + (bounds.Height * fraction)).ToArray();

            foreach (var x in xGrid)
            {
                foreach (var y in yGrid)
                {
                    points.Add(new ProbePoint(x, y));
                }
            }
        }

        return points
            .GroupBy(point => $"{Math.Round(point.X):F0}:{Math.Round(point.Y):F0}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static string BuildElementIdentityKey(IWindowsUiAutomationElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.RuntimeId))
        {
            return $"runtime:{element.RuntimeId}";
        }

        if (element.NativeWindowHandle is not null)
        {
            return $"hwnd:{element.NativeWindowHandle.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        return string.Join(
            "|",
            "fallback",
            element.FrameworkId ?? string.Empty,
            element.ClassName ?? string.Empty,
            element.AutomationId ?? string.Empty,
            element.Name ?? string.Empty,
            element.Role);
    }

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        var materialized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return materialized.Length == 0 ? null : string.Join(",", materialized);
    }

    private NativeUiAutomationElementSnapshot CaptureElement(
        IWindowsUiAutomationElement element,
        NativeUiAutomationCaptureOptions options,
        int depth,
        bool isRoot,
        CaptureStats stats,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stats.NodeCount++;
        stats.MaxDepth = Math.Max(stats.MaxDepth, depth);

        var children = new List<NativeUiAutomationElementSnapshot>();
        var scannedChildCount = 0;
        var includedChildCount = 0;

        if (depth < options.MaxDepth)
        {
            var child = _platform.GetFirstChild(element, options.TreeView);

            while (child is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (scannedChildCount >= options.MaxChildrenPerNode || includedChildCount >= options.MaxChildrenPerNode)
                {
                    stats.MarkTruncated("MaxChildrenPerNode");
                    break;
                }

                scannedChildCount++;

                switch (GetInclusionDecision(child, options))
                {
                    case ChildInclusionDecision.Include:
                        children.Add(CaptureElement(child, options, depth + 1, isRoot: false, stats, cancellationToken));
                        includedChildCount++;
                        break;
                    case ChildInclusionDecision.FilteredByOffscreen:
                        stats.ChildrenFilteredByOffscreen++;
                        break;
                    case ChildInclusionDecision.FilteredByFramework:
                        stats.ChildrenFilteredByFramework++;
                        break;
                }

                child = _platform.GetNextSibling(child, options.TreeView);
            }
        }
        else if (_platform.GetFirstChild(element, options.TreeView) is not null)
        {
            stats.MarkTruncated("MaxDepth");
        }

        if (isRoot)
        {
            stats.RootScannedChildCount = scannedChildCount;
            stats.RootIncludedChildCount = includedChildCount;
        }

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["localizedControlType"] = element.LocalizedControlType,
            ["nativeWindowHandle"] = element.NativeWindowHandle?.ToString(CultureInfo.InvariantCulture),
            ["processId"] = element.ProcessId?.ToString(CultureInfo.InvariantCulture),
            ["isPassword"] = element.IsPassword.ToString()
        };

        return new NativeUiAutomationElementSnapshot(
            element.Role,
            element.Name,
            element.AutomationId,
            element.RuntimeId,
            element.FrameworkId,
            element.ClassName,
            element.IsEnabled,
            element.IsOffscreen,
            element.HasKeyboardFocus,
            element.IsSelected,
            element.Value,
            element.Bounds,
            metadata,
            children);
    }

    private static ChildInclusionDecision GetInclusionDecision(
        IWindowsUiAutomationElement element,
        NativeUiAutomationCaptureOptions options)
    {
        if (!options.IncludeOffscreenNodes && element.IsOffscreen)
        {
            return ChildInclusionDecision.FilteredByOffscreen;
        }

        if (options.AllowedFrameworkIds.Count == 0)
        {
            return ChildInclusionDecision.Include;
        }

        return !string.IsNullOrWhiteSpace(element.FrameworkId) && options.AllowedFrameworkIds.Contains(element.FrameworkId)
            ? ChildInclusionDecision.Include
            : ChildInclusionDecision.FilteredByFramework;
    }

    private static NativeUiAutomationCaptureOptions CreateDiagnosticFallbackOptions(NativeUiAutomationCaptureOptions options) =>
        options with
        {
            TreeView = "Raw",
            IncludeOffscreenNodes = true,
            AllowedFrameworkIds = options.PreserveFrameworkFilterOnDiagnosticFallback
                ? options.AllowedFrameworkIds
                : EmptyFrameworkFilter
        };

    private static string NormalizeTreeView(string? treeView) =>
        string.Equals(treeView, "Raw", StringComparison.OrdinalIgnoreCase)
            ? "Raw"
            : "Control";

    private enum ChildInclusionDecision
    {
        Include,
        FilteredByOffscreen,
        FilteredByFramework
    }

    private sealed record ProbePoint(double X, double Y);

    private sealed record PointProbeDiagnostics(
        bool Enabled,
        int PointCount,
        IReadOnlyDictionary<string, IWindowsUiAutomationElement> DistinctElements)
    {
        public static PointProbeDiagnostics Disabled { get; } =
            new(false, 0, new Dictionary<string, IWindowsUiAutomationElement>(StringComparer.Ordinal));
    }

    private sealed class CaptureStats
    {
        private readonly HashSet<string> _truncationReasons = new(StringComparer.Ordinal);

        public int NodeCount { get; set; }

        public int MaxDepth { get; set; }

        public bool Truncated { get; private set; }

        public int RootScannedChildCount { get; set; }

        public int RootIncludedChildCount { get; set; }

        public int ChildrenFilteredByOffscreen { get; set; }

        public int ChildrenFilteredByFramework { get; set; }

        public void MarkTruncated(string reason)
        {
            Truncated = true;
            _truncationReasons.Add(reason);
        }

        public string? GetTruncationReason() =>
            _truncationReasons.Count == 0
                ? null
                : string.Join(",", _truncationReasons.OrderBy(static reason => reason, StringComparer.Ordinal));
    }
}
