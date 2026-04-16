using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class DefaultTargetBehaviorPackResolver : ITargetBehaviorPackResolver
{
    private readonly IReadOnlyDictionary<string, ITargetBehaviorPack> _packsByName;

    public DefaultTargetBehaviorPackResolver(IEnumerable<ITargetBehaviorPack> packs)
    {
        _packsByName = packs.ToDictionary(static pack => pack.PackName, StringComparer.OrdinalIgnoreCase);
    }

    public TargetBehaviorPackSelection? ResolveSelection(ResolvedDesktopTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Target.Metadata.TryGetValue(DesktopTargetMetadata.BehaviorPack, out var packName) || string.IsNullOrWhiteSpace(packName))
        {
            return null;
        }

        return new TargetBehaviorPackSelection(packName.Trim(), DesktopTargetMetadata.BehaviorPack);
    }

    public ITargetBehaviorPack? ResolvePack(string packName) =>
        string.IsNullOrWhiteSpace(packName) ? null : _packsByName.TryGetValue(packName.Trim(), out var pack) ? pack : null;
}
