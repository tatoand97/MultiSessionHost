using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record ResolvedDesktopTargetContext(
    SessionId SessionId,
    DesktopTargetProfile Profile,
    SessionTargetBinding Binding,
    DesktopSessionTarget Target,
    IReadOnlyDictionary<string, string> Variables);
