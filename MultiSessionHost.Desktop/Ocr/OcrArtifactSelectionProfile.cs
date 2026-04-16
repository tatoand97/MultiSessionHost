namespace MultiSessionHost.Desktop.Ocr;

internal sealed record OcrArtifactSelectionProfile(
    string ProfileName,
    IReadOnlyList<string> RegionNames,
    IReadOnlyList<string> PreferredArtifactKinds,
    bool IncludeFullFrameFallback,
    bool MergeMultipleAttempts,
    string StrategyName)
{
    public static OcrArtifactSelectionProfile DefaultRegionOcr =>
        new(
            "DefaultRegionOcr",
            ["window.top", "window.center", "window.left", "window.right"],
            ["threshold", "high-contrast", "grayscale", "raw"],
            IncludeFullFrameFallback: false,
            MergeMultipleAttempts: false,
            "DefaultRegionAwareOcrSelectionV1");

    public static OcrArtifactSelectionProfile DefaultRegionOcrWithFullFrameFallback =>
        DefaultRegionOcr with
        {
            ProfileName = "DefaultRegionOcrWithFullFrameFallback",
            IncludeFullFrameFallback = true
        };
}
