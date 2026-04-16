using System.Windows;
using System.Windows.Automation;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

public sealed class WindowsUiAutomationElementProvider : INativeUiAutomationElementProvider
{
    public INativeUiAutomationElement GetRoot(DesktopSessionAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows UI Automation interaction is only supported on Windows.");
        }

        if (attachment.Window.WindowHandle == 0)
        {
            throw new InvalidOperationException($"Session '{attachment.SessionId}' does not have a window handle for native UI Automation interaction.");
        }

        var element = AutomationElement.FromHandle(new IntPtr(attachment.Window.WindowHandle))
            ?? throw new InvalidOperationException($"No UI Automation root was found for window handle '{attachment.Window.WindowHandle}'.");

        return new WindowsUiAutomationElement(element);
    }
}

internal sealed class WindowsUiAutomationElement : INativeUiAutomationElement
{
    private readonly AutomationElement _element;

    public WindowsUiAutomationElement(AutomationElement element)
    {
        _element = element;
    }

    public string Role => SafeControlType(_element);

    public string? Name => SafeString(_element, AutomationElement.NameProperty);

    public string? AutomationId => SafeString(_element, AutomationElement.AutomationIdProperty);

    public string? RuntimeId => SafeRuntimeId(_element);

    public string? FrameworkId => SafeString(_element, AutomationElement.FrameworkIdProperty);

    public string? ClassName => SafeString(_element, AutomationElement.ClassNameProperty);

    public bool IsEnabled => SafeBool(_element, AutomationElement.IsEnabledProperty, defaultValue: false);

    public bool IsOffscreen => SafeBool(_element, AutomationElement.IsOffscreenProperty, defaultValue: false);

    public bool HasKeyboardFocus => SafeBool(_element, AutomationElement.HasKeyboardFocusProperty, defaultValue: false);

    public bool? IsSelected => SafeSelection(_element);

    public string? Value => SafeValue(_element);

    public UiBounds? Bounds => SafeBounds(_element);

    public IReadOnlyDictionary<string, string?> Metadata =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["localizedControlType"] = SafeString(_element, AutomationElement.LocalizedControlTypeProperty),
            ["nativeWindowHandle"] = SafeInt(_element, AutomationElement.NativeWindowHandleProperty)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["processId"] = SafeInt(_element, AutomationElement.ProcessIdProperty)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isPassword"] = SafeBool(_element, AutomationElement.IsPasswordProperty, defaultValue: false).ToString()
        };

    public IReadOnlyList<INativeUiAutomationElement> GetChildren(NativeUiAutomationCaptureOptions options)
    {
        var result = new List<INativeUiAutomationElement>();
        var walker = string.Equals(options.TreeView, "Raw", StringComparison.OrdinalIgnoreCase)
            ? TreeWalker.RawViewWalker
            : TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(_element);
        var scanned = 0;

        while (child is not null && scanned < options.MaxChildrenPerNode)
        {
            scanned++;
            var wrapped = new WindowsUiAutomationElement(child);

            if (options.IncludeOffscreenNodes || !wrapped.IsOffscreen)
            {
                if (options.AllowedFrameworkIds.Count == 0 ||
                    !string.IsNullOrWhiteSpace(wrapped.FrameworkId) && options.AllowedFrameworkIds.Contains(wrapped.FrameworkId))
                {
                    result.Add(wrapped);
                }
            }

            child = walker.GetNextSibling(child);
        }

        return result;
    }

    public bool TrySetFocus() => Try(() =>
    {
        _element.SetFocus();
        return true;
    });

    public bool TryInvoke() => TryPattern<InvokePattern>(InvokePattern.Pattern, pattern =>
    {
        pattern.Invoke();
        return true;
    });

    public bool TrySelect() => TryPattern<SelectionItemPattern>(SelectionItemPattern.Pattern, pattern =>
    {
        pattern.Select();
        return true;
    });

    public bool TryExpand() => TryPattern<ExpandCollapsePattern>(ExpandCollapsePattern.Pattern, pattern =>
    {
        if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
        {
            pattern.Expand();
        }

        return true;
    });

    public bool TryCollapse() => TryPattern<ExpandCollapsePattern>(ExpandCollapsePattern.Pattern, pattern =>
    {
        if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Collapsed)
        {
            pattern.Collapse();
        }

        return true;
    });

    public bool TryToggle() => TryPattern<TogglePattern>(TogglePattern.Pattern, pattern =>
    {
        pattern.Toggle();
        return true;
    });

    public bool TrySetValue(string value) => TryPattern<ValuePattern>(ValuePattern.Pattern, pattern =>
    {
        if (pattern.Current.IsReadOnly)
        {
            return false;
        }

        pattern.SetValue(value);
        return true;
    });

    public bool TryLegacyDefaultAction() => false;

    private bool TryPattern<TPattern>(AutomationPattern automationPattern, Func<TPattern, bool> action)
        where TPattern : class
    {
        try
        {
            return _element.TryGetCurrentPattern(automationPattern, out var pattern) &&
                pattern is TPattern typed &&
                action(typed);
        }
        catch (ElementNotAvailableException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (ElementNotAvailableException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

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
            return rectangle == Rect.Empty
                ? null
                : new UiBounds(
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
