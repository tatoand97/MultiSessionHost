using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class ConfiguredDesktopTargetProfileCatalog : IDesktopTargetProfileCatalog
{
    private readonly IReadOnlyDictionary<string, DesktopTargetProfile> _profilesByName;

    public ConfiguredDesktopTargetProfileCatalog(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _profilesByName = options.DesktopTargets
            .Select(SessionTargetBindingModelMapper.MapProfile)
            .ToDictionary(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() =>
        _profilesByName.Values
            .OrderBy(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public DesktopTargetProfile? TryGetProfile(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return _profilesByName.TryGetValue(profileName.Trim(), out var profile) ? profile : null;
    }
}
