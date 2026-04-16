using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Automation;

public sealed record NativeUiAutomationCaptureOptions(
    int MaxDepth,
    int MaxChildrenPerNode,
    bool IncludeOffscreenNodes,
    string TreeView,
    IReadOnlySet<string> AllowedFrameworkIds,
    bool PreserveFrameworkFilterOnDiagnosticFallback = false,
    bool EnablePointProbe = true,
    int PointProbeInsetPixels = 24,
    bool EnablePointProbeGrid = true)
{
    public static NativeUiAutomationCaptureOptions FromMetadata(IReadOnlyDictionary<string, string?> metadata) =>
        new(
            GetPositiveInt(metadata, "NativeUiAutomation.MaxDepth", 8),
            GetPositiveInt(metadata, "NativeUiAutomation.MaxChildrenPerNode", 200),
            GetBool(metadata, "NativeUiAutomation.IncludeOffscreenNodes", false),
            GetString(metadata, "NativeUiAutomation.TreeView", "Control"),
            GetSet(metadata, "NativeUiAutomation.AllowedFrameworkIds"),
            GetBool(metadata, "NativeUiAutomation.PreserveFrameworkFilterOnDiagnosticFallback", false),
            GetBool(metadata, "NativeUiAutomation.EnablePointProbe", true),
            GetPositiveInt(metadata, "NativeUiAutomation.PointProbeInsetPixels", 24),
            GetBool(metadata, "NativeUiAutomation.EnablePointProbeGrid", true));

    private static int GetPositiveInt(IReadOnlyDictionary<string, string?> metadata, string key, int defaultValue) =>
        metadata.TryGetValue(key, out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
        parsed > 0
            ? parsed
            : defaultValue;

    private static bool GetBool(IReadOnlyDictionary<string, string?> metadata, string key, bool defaultValue) =>
        metadata.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string GetString(IReadOnlyDictionary<string, string?> metadata, string key, string defaultValue) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : defaultValue;

    private static IReadOnlySet<string> GetSet(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record NativeUiAutomationRawSnapshot(
    NativeUiAutomationNode Root,
    int NodeCount,
    int MaxDepth,
    bool Truncated,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record NativeUiAutomationNode(
    string NodeId,
    string IdentityQuality,
    string IdentityBasis,
    string Role,
    string? Name,
    string? AutomationId,
    string? RuntimeId,
    string? FrameworkId,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool? IsSelected,
    string? Value,
    UiBounds? Bounds,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<NativeUiAutomationNode> Children);

public sealed record NativeUiAutomationElementSnapshot(
    string Role,
    string? Name,
    string? AutomationId,
    string? RuntimeId,
    string? FrameworkId,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool? IsSelected,
    string? Value,
    UiBounds? Bounds,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<NativeUiAutomationElementSnapshot> Children);

public sealed record NativeUiNodeIdentity(string NodeId, string Quality, string Basis);
