namespace MultiSessionHost.Desktop.Preprocessing;

internal sealed record FramePreprocessingArtifactSelection(
    string ProfileName,
    IReadOnlyList<string> RegionNames,
    bool IncludeThreshold,
    string StrategyName)
{
    public static FramePreprocessingArtifactSelection Default =>
        new(
            "DefaultFramePreprocessing",
            ["window.top", "window.center", "window.left", "window.right"],
            IncludeThreshold: false,
            "DefaultDeterministicArtifactSelectionV1");
}
