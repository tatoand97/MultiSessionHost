namespace MultiSessionHost.Desktop.Regions;

public sealed class InsetDesktopGridRegionLocator : DesktopGridRegionLocatorBase
{
    public override string LocatorName => nameof(InsetDesktopGridRegionLocator);

    public override string RegionLayoutProfile => "InsetDesktopGrid";

    protected override double TopStripRatio => 0.08d;

    protected override double BottomStripRatio => 0.08d;

    protected override double SidePanelRatio => 0.12d;

    protected override double SafeInsetRatio => 0.05d;

    public override int Priority => 20;
}