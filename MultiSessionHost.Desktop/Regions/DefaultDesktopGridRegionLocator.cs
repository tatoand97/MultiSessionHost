namespace MultiSessionHost.Desktop.Regions;

public sealed class DefaultDesktopGridRegionLocator : DesktopGridRegionLocatorBase
{
    public override string LocatorName => nameof(DefaultDesktopGridRegionLocator);

    public override string RegionLayoutProfile => "DefaultDesktopGrid";

    protected override double TopStripRatio => 0.12d;

    protected override double BottomStripRatio => 0.12d;

    protected override double SidePanelRatio => 0.18d;

    protected override double SafeInsetRatio => 0.03d;

    public override int Priority => 10;
}