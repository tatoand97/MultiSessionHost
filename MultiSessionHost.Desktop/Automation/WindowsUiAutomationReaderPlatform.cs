using System.Windows;
using System.Windows.Automation;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

internal interface IWindowsUiAutomationReaderPlatform
{
    IWindowsUiAutomationElement GetRoot(long windowHandle);

    IWindowsUiAutomationElement? GetFirstChild(IWindowsUiAutomationElement element, string treeView);

    IWindowsUiAutomationElement? GetNextSibling(IWindowsUiAutomationElement element, string treeView);

    IWindowsUiAutomationElement? GetParent(IWindowsUiAutomationElement element, string? treeView = null);

    IWindowsUiAutomationElement? GetElementFromPoint(double x, double y);
}

internal interface IWindowsUiAutomationElement
{
    string Role { get; }

    string? Name { get; }

    string? AutomationId { get; }

    string? RuntimeId { get; }

    string? FrameworkId { get; }

    string? ClassName { get; }

    bool IsEnabled { get; }

    bool IsOffscreen { get; }

    bool HasKeyboardFocus { get; }

    bool? IsSelected { get; }

    string? Value { get; }

    UiBounds? Bounds { get; }

    string? LocalizedControlType { get; }

    int? NativeWindowHandle { get; }

    int? ProcessId { get; }

    bool IsPassword { get; }
}

internal sealed class WindowsUiAutomationReaderPlatform : IWindowsUiAutomationReaderPlatform
{
    public IWindowsUiAutomationElement GetRoot(long windowHandle)
    {
        var element = AutomationElement.FromHandle(new IntPtr(windowHandle))
            ?? throw new InvalidOperationException($"No UI Automation root was found for window handle '{windowHandle}'.");

        return new WindowsUiAutomationReaderElement(element);
    }

    public IWindowsUiAutomationElement? GetFirstChild(IWindowsUiAutomationElement element, string treeView)
    {
        var child = GetWalker(treeView).GetFirstChild(Unwrap(element));
        return child is null ? null : new WindowsUiAutomationReaderElement(child);
    }

    public IWindowsUiAutomationElement? GetNextSibling(IWindowsUiAutomationElement element, string treeView)
    {
        var sibling = GetWalker(treeView).GetNextSibling(Unwrap(element));
        return sibling is null ? null : new WindowsUiAutomationReaderElement(sibling);
    }

    public IWindowsUiAutomationElement? GetParent(IWindowsUiAutomationElement element, string? treeView = null)
    {
        try
        {
            var walker = string.IsNullOrWhiteSpace(treeView)
                ? TreeWalker.RawViewWalker
                : GetWalker(treeView);
            var parent = walker.GetParent(Unwrap(element));
            return parent is null ? null : new WindowsUiAutomationReaderElement(parent);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public IWindowsUiAutomationElement? GetElementFromPoint(double x, double y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new Point(x, y));
            return element is null ? null : new WindowsUiAutomationReaderElement(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AutomationElement Unwrap(IWindowsUiAutomationElement element) =>
        element is WindowsUiAutomationReaderElement wrapped
            ? wrapped.Element
            : throw new InvalidOperationException("The provided UI Automation element does not belong to the Windows reader platform.");

    private static TreeWalker GetWalker(string treeView) =>
        string.Equals(treeView, "Raw", StringComparison.OrdinalIgnoreCase)
            ? TreeWalker.RawViewWalker
            : TreeWalker.ControlViewWalker;
}

internal sealed class WindowsUiAutomationReaderElement : IWindowsUiAutomationElement
{
    public WindowsUiAutomationReaderElement(AutomationElement element)
    {
        Element = element;
    }

    public AutomationElement Element { get; }

    public string Role => SafeControlType(Element);

    public string? Name => SafeString(Element, AutomationElement.NameProperty);

    public string? AutomationId => SafeString(Element, AutomationElement.AutomationIdProperty);

    public string? RuntimeId => SafeRuntimeId(Element);

    public string? FrameworkId => SafeString(Element, AutomationElement.FrameworkIdProperty);

    public string? ClassName => SafeString(Element, AutomationElement.ClassNameProperty);

    public bool IsEnabled => SafeBool(Element, AutomationElement.IsEnabledProperty, defaultValue: false);

    public bool IsOffscreen => SafeBool(Element, AutomationElement.IsOffscreenProperty, defaultValue: false);

    public bool HasKeyboardFocus => SafeBool(Element, AutomationElement.HasKeyboardFocusProperty, defaultValue: false);

    public bool? IsSelected => SafeSelection(Element);

    public string? Value => SafeValue(Element);

    public UiBounds? Bounds => SafeBounds(Element);

    public string? LocalizedControlType => SafeString(Element, AutomationElement.LocalizedControlTypeProperty);

    public int? NativeWindowHandle => SafeInt(Element, AutomationElement.NativeWindowHandleProperty);

    public int? ProcessId => SafeInt(Element, AutomationElement.ProcessIdProperty);

    public bool IsPassword => SafeBool(Element, AutomationElement.IsPasswordProperty, defaultValue: false);

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
}
