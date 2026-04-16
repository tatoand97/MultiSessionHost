using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Ocr;

public interface IOcrExtractionService
{
    ValueTask<SessionOcrExtractionResult?> ExtractLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken);
}
