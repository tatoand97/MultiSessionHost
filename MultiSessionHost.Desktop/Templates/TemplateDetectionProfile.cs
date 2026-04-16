namespace MultiSessionHost.Desktop.Templates;

public sealed record TemplateDetectionProfile(
    string ProfileName,
    IReadOnlyList<string> RegionNames,
    IReadOnlyList<string> PreferredArtifactKinds,
    bool IncludeFullFrameFallback,
    string TemplateSetName,
    string StrategyName)
{
    public static TemplateDetectionProfile DefaultRegionTemplateDetection =>
        new(
            "DefaultRegionTemplateDetection",
            ["window.top", "window.center", "window.left", "window.right"],
            ["threshold", "high-contrast", "grayscale", "raw"],
            IncludeFullFrameFallback: false,
            TemplateSetName: "DefaultGenericMarkers",
            StrategyName: "DefaultRegionAwareTemplateSelectionV1");

    public static TemplateDetectionProfile DefaultRegionTemplateDetectionWithFullFrameFallback =>
        DefaultRegionTemplateDetection with
        {
            ProfileName = "DefaultRegionTemplateDetectionWithFullFrameFallback",
            IncludeFullFrameFallback = true
        };
}
