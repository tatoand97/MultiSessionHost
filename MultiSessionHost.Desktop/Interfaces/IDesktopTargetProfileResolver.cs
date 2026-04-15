using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IDesktopTargetProfileResolver
{
    IReadOnlyCollection<DesktopTargetProfile> GetProfiles();

    DesktopTargetProfile? TryGetProfile(string profileName);

    SessionTargetBinding? TryGetBinding(SessionId sessionId);

    ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot);
}
