using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Extraction;

public interface ISessionSemanticExtractionStore
{
    ValueTask InitializeAsync(SessionId sessionId, UiSemanticExtractionResult result, CancellationToken cancellationToken);

    ValueTask<UiSemanticExtractionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyCollection<UiSemanticExtractionResult>> GetAllAsync(CancellationToken cancellationToken);

    ValueTask<UiSemanticExtractionResult> UpdateAsync(SessionId sessionId, UiSemanticExtractionResult result, CancellationToken cancellationToken);

    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}
