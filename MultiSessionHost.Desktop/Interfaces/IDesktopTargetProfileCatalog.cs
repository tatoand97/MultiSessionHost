using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetProfileCatalog
{
    IReadOnlyCollection<DesktopTargetProfile> GetProfiles();

    DesktopTargetProfile? TryGetProfile(string profileName);
}
