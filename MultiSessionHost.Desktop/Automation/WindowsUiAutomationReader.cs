using System.Windows;
using System.Windows.Automation;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

public sealed class WindowsUiAutomationReader : INativeUiAutomationReader
{
    private readonly NativeUiAutomationIdentityBuilder _identityBuilder;

    public WindowsUiAutomationReader(NativeUiAutomationIdentityBuilder identityBuilder)
    {
        _identityBuilder = identityBuilder;
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
        var rootElement = AutomationElement.FromHandle(new IntPtr(attachment.Window.WindowHandle))
            ?? throw new InvalidOperationException($"No UI Automation root was found for window handle '{attachment.Window.WindowHandle}'.");

        var stats = new CaptureStats();
        var rawRoot = CaptureElement(rootElement, options, depth: 0, stats, cancellationToken);
        var root = _identityBuilder.AssignIdentities(rawRoot);
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["captureSource"] = "WindowsUiAutomation",
            ["automationBackend"] = "System.Windows.Automation",
            ["treeView"] = options.TreeView,
            ["maxDepth"] = options.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxChildrenPerNode"] = options.MaxChildrenPerNode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["includeOffscreenNodes"] = options.IncludeOffscreenNodes.ToString(),
            ["rootRuntimeId"] = SafeRuntimeId(rootElement),
            ["rootAutomationId"] = SafeString(rootElement, AutomationElement.AutomationIdProperty),
            ["rootFrameworkId"] = SafeString(rootElement, AutomationElement.FrameworkIdProperty)
        };

        return Task.FromResult(new NativeUiAutomationRawSnapshot(root, stats.NodeCount, stats.MaxDepth, stats.Truncated, metadata));
    }

    private static NativeUiAutomationElementSnapshot CaptureElement(
        AutomationElement element,
        NativeUiAutomationCaptureOptions options,
        int depth,
        CaptureStats stats,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stats.NodeCount++;
        stats.MaxDepth = Math.Max(stats.MaxDepth, depth);

        var role = SafeControlType(element);
        var name = SafeString(element, AutomationElement.NameProperty);
        var automationId = SafeString(element, AutomationElement.AutomationIdProperty);
        var runtimeId = SafeRuntimeId(element);
        var frameworkId = SafeString(element, AutomationElement.FrameworkIdProperty);
        var className = SafeString(element, AutomationElement.ClassNameProperty);
        var isEnabled = SafeBool(element, AutomationElement.IsEnabledProperty, defaultValue: false);
        var isOffscreen = SafeBool(element, AutomationElement.IsOffscreenProperty, defaultValue: false);
        var hasKeyboardFocus = SafeBool(element, AutomationElement.HasKeyboardFocusProperty, defaultValue: false);
        var isSelected = SafeSelection(element);
        var value = SafeValue(element);
        var bounds = SafeBounds(element);
        var children = new List<NativeUiAutomationElementSnapshot>();

        if (depth < options.MaxDepth)
        {
            var walker = GetWalker(options.TreeView);
            var child = walker.GetFirstChild(element);
            var childCount = 0;
            var scannedChildCount = 0;

            while (child is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (childCount >= options.MaxChildrenPerNode || scannedChildCount >= options.MaxChildrenPerNode)
                {
                    stats.Truncated = true;
                    break;
                }

                scannedChildCount++;

                if (ShouldInclude(child, options))
                {
                    children.Add(CaptureElement(child, options, depth + 1, stats, cancellationToken));
                    childCount++;
                }

                child = walker.GetNextSibling(child);
            }
        }
        else
        {
            stats.Truncated = true;
        }

        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["localizedControlType"] = SafeString(element, AutomationElement.LocalizedControlTypeProperty),
            ["nativeWindowHandle"] = SafeInt(element, AutomationElement.NativeWindowHandleProperty)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["processId"] = SafeInt(element, AutomationElement.ProcessIdProperty)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isPassword"] = SafeBool(element, AutomationElement.IsPasswordProperty, defaultValue: false).ToString()
        };

        return new NativeUiAutomationElementSnapshot(
            role,
            name,
            automationId,
            runtimeId,
            frameworkId,
            className,
            isEnabled,
            isOffscreen,
            hasKeyboardFocus,
            isSelected,
            value,
            bounds,
            metadata,
            children);
    }

    private static bool ShouldInclude(AutomationElement element, NativeUiAutomationCaptureOptions options)
    {
        if (!options.IncludeOffscreenNodes && SafeBool(element, AutomationElement.IsOffscreenProperty, defaultValue: false))
        {
            return false;
        }

        if (options.AllowedFrameworkIds.Count == 0)
        {
            return true;
        }

        var frameworkId = SafeString(element, AutomationElement.FrameworkIdProperty);
        return !string.IsNullOrWhiteSpace(frameworkId) && options.AllowedFrameworkIds.Contains(frameworkId);
    }

    private static TreeWalker GetWalker(string treeView) =>
        string.Equals(treeView, "Raw", StringComparison.OrdinalIgnoreCase)
            ? TreeWalker.RawViewWalker
            : TreeWalker.ControlViewWalker;

    private static string SafeControlType(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType.ProgrammaticName.Split('.').LastOrDefault() ?? "Custom";
        }
        catch (ElementNotAvailableException)
        {
            return "Unavailable";
        }
    }

    private static string? SafeString(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool SafeBool(AutomationElement element, AutomationProperty property, bool defaultValue)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return value is bool flag ? flag : defaultValue;
        }
        catch (ElementNotAvailableException)
        {
            return defaultValue;
        }
    }

    private static int? SafeInt(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return value is int number ? number : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string? SafeRuntimeId(AutomationElement element)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            return runtimeId.Length == 0 ? null : string.Join(".", runtimeId);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool? SafeSelection(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                ? ((SelectionItemPattern)pattern).Current.IsSelected
                : null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static string? SafeValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
            {
                return ((ValuePattern)valuePattern).Current.Value;
            }

            if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rangePattern))
            {
                return ((RangeValuePattern)rangePattern).Current.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var togglePattern))
            {
                return ((TogglePattern)togglePattern).Current.ToggleState.ToString();
            }
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        return null;
    }

    private static UiBounds? SafeBounds(AutomationElement element)
    {
        try
        {
            var rectangle = element.Current.BoundingRectangle;

            if (rectangle == Rect.Empty)
            {
                return null;
            }

            return new UiBounds(
                (int)Math.Round(rectangle.X),
                (int)Math.Round(rectangle.Y),
                Math.Max(0, (int)Math.Round(rectangle.Width)),
                Math.Max(0, (int)Math.Round(rectangle.Height)));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private sealed class CaptureStats
    {
        public int NodeCount { get; set; }

        public int MaxDepth { get; set; }

        public bool Truncated { get; set; }
    }
}
