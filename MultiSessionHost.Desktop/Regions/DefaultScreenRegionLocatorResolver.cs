using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;

namespace MultiSessionHost.Desktop.Regions;

public sealed class DefaultScreenRegionLocatorResolver : IScreenRegionLocatorResolver
{
    private readonly IReadOnlyList<IScreenRegionLocator> _locators;

    public DefaultScreenRegionLocatorResolver(IEnumerable<IScreenRegionLocator> locators)
    {
        ArgumentNullException.ThrowIfNull(locators);
        _locators = locators.ToArray();
    }

    public IScreenRegionLocator Resolve(ResolvedDesktopTargetContext context, SessionScreenSnapshot snapshot, string regionLayoutProfile)
    {
        var locator = _locators
            .Where(locator => locator.Supports(context, snapshot, regionLayoutProfile))
            .OrderByDescending(static locator => locator is IRegionLocatorPriority priority ? priority.Priority : 0)
            .FirstOrDefault();

        return locator ?? throw new InvalidOperationException($"No screen region locator is registered for target kind '{context.Target.Kind}' and region layout profile '{regionLayoutProfile}'.");
    }
}

internal interface IRegionLocatorPriority
{
    int Priority { get; }
}