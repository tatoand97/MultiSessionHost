using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Templates;

public interface ITemplateDetectionService
{
    ValueTask<SessionTemplateDetectionResult?> DetectLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken);
}
