using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record SessionTargetBinding(
    SessionId SessionId,
    string TargetProfileName,
    IReadOnlyDictionary<string, string> Variables,
    DesktopTargetProfileOverride? Overrides);
