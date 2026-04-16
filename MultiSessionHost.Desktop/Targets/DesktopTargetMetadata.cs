namespace MultiSessionHost.Desktop.Targets;

internal static class DesktopTargetMetadata
{
    public const string StatePath = "StatePath";
    public const string TickPath = "TickPath";
    public const string UiSnapshotPath = "UiSnapshotPath";
    public const string UiSource = "UiSource";
    public const string SemanticPackage = "SemanticPackage";
    public const string BehaviorPack = "BehaviorPack";
    public const string UiClickNodePathTemplate = "UiClickNodePathTemplate";
    public const string UiInvokeNodePathTemplate = "UiInvokeNodePathTemplate";
    public const string UiSetTextNodePathTemplate = "UiSetTextNodePathTemplate";
    public const string UiToggleNodePathTemplate = "UiToggleNodePathTemplate";
    public const string UiSelectNodePathTemplate = "UiSelectNodePathTemplate";

    public static string GetValue(IReadOnlyDictionary<string, string?> metadata, string key, string defaultValue) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
}
