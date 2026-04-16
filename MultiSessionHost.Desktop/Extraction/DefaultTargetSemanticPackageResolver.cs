using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class DefaultTargetSemanticPackageResolver : ITargetSemanticPackageResolver
{
    private readonly IReadOnlyDictionary<string, ITargetSemanticPackage> _packagesByName;

    public DefaultTargetSemanticPackageResolver(IEnumerable<ITargetSemanticPackage> packages)
    {
        _packagesByName = packages.ToDictionary(static package => package.PackageName, StringComparer.OrdinalIgnoreCase);
    }

    public TargetSemanticPackageSelection? ResolveSelection(ResolvedDesktopTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var packageName = DesktopTargetMetadata.GetValue(context.Profile.Metadata, DesktopTargetMetadata.SemanticPackage, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(packageName)
            ? null
            : new TargetSemanticPackageSelection(packageName, DesktopTargetMetadata.SemanticPackage);
    }

    public ITargetSemanticPackage? ResolvePackage(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return _packagesByName.TryGetValue(packageName.Trim(), out var package) ? package : null;
    }
}