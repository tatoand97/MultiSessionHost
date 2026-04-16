using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public interface IFramePreprocessingService
{
    ValueTask<SessionFramePreprocessingResult?> PreprocessLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken);
}
