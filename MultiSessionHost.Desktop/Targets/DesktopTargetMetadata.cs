namespace MultiSessionHost.Desktop.Targets;

internal static class DesktopTargetMetadata
{
    public const string StatePath = "StatePath";
    public const string TickPath = "TickPath";
    public const string UiSnapshotPath = "UiSnapshotPath";
    public const string UiSource = "UiSource";
    public const string ObservabilityBackend = "ObservabilityBackend";
    public const string RegionLayoutProfile = "RegionLayoutProfile";
    public const string EnableFramePreprocessing = "EnableFramePreprocessing";
    public const string FramePreprocessingProfile = "FramePreprocessingProfile";
    public const string FramePreprocessingRegionSet = "FramePreprocessingRegionSet";
    public const string FramePreprocessingIncludeThreshold = "FramePreprocessingIncludeThreshold";
    public const string EnableOcr = "EnableOcr";
    public const string OcrProfile = "OcrProfile";
    public const string OcrRegionSet = "OcrRegionSet";
    public const string OcrPreferredArtifactKinds = "OcrPreferredArtifactKinds";
    public const string OcrIncludeFullFrameFallback = "OcrIncludeFullFrameFallback";
    public const string EnableTemplateDetection = "EnableTemplateDetection";
    public const string TemplateDetectionProfile = "TemplateDetectionProfile";
    public const string TemplateRegionSet = "TemplateRegionSet";
    public const string TemplatePreferredArtifactKinds = "TemplatePreferredArtifactKinds";
    public const string TemplateSet = "TemplateSet";
    public const string TemplateIncludeFullFrameFallback = "TemplateIncludeFullFrameFallback";
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
