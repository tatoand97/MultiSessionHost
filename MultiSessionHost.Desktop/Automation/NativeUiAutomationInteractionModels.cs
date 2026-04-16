using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

public interface INativeUiAutomationElement
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

    IReadOnlyDictionary<string, string?> Metadata { get; }

    IReadOnlyList<INativeUiAutomationElement> GetChildren(NativeUiAutomationCaptureOptions options);

    bool TrySetFocus();

    bool TryInvoke();

    bool TrySelect();

    bool TryExpand();

    bool TryCollapse();

    bool TryToggle();

    bool TrySetValue(string value);

    bool TryLegacyDefaultAction();
}

public interface INativeUiAutomationElementProvider
{
    INativeUiAutomationElement GetRoot(DesktopSessionAttachment attachment);
}

public interface INativeUiAutomationElementLocator
{
    Task<LocatedNativeUiElement> LocateAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken);
}

public sealed record LocatedNativeUiElement(
    INativeUiAutomationElement Element,
    NativeUiAutomationNode Node,
    string MatchStrategy,
    bool IsExactNodeIdMatch);

public interface INativeInputFallbackExecutor
{
    Task<NativeInputFallbackResult> ClickAsync(
        INativeUiAutomationElement element,
        ResolvedUiAction action,
        CancellationToken cancellationToken);

    Task<NativeInputFallbackResult> SetTextAsync(
        INativeUiAutomationElement element,
        ResolvedUiAction action,
        CancellationToken cancellationToken);
}

public sealed record NativeInputFallbackResult(bool Succeeded, string Message);
