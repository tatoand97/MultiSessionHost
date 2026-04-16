namespace MultiSessionHost.Desktop.Regions;

public sealed record ScreenRegionSet(
    string RegionLayoutProfile,
    string LocatorSetName,
    IReadOnlyList<ScreenRegionMatch> Regions);