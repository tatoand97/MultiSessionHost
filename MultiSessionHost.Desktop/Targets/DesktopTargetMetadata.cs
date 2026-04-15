namespace MultiSessionHost.Desktop.Targets;

internal static class DesktopTargetMetadata
{
    public const string StatePath = "StatePath";
    public const string TickPath = "TickPath";
    public const string UiSnapshotPath = "UiSnapshotPath";
    public const string UiSource = "UiSource";

    public static string GetValue(IReadOnlyDictionary<string, string?> metadata, string key, string defaultValue) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
}
