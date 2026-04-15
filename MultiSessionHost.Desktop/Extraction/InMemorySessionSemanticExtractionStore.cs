using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class InMemorySessionSemanticExtractionStore : ISessionSemanticExtractionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, UiSemanticExtractionResult> _results = [];

    public ValueTask InitializeAsync(SessionId sessionId, UiSemanticExtractionResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_results.ContainsKey(sessionId))
            {
                throw new InvalidOperationException($"Semantic extraction result for session '{sessionId}' is already initialized.");
            }

            _results[sessionId] = result;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<UiSemanticExtractionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_results.TryGetValue(sessionId, out var result) ? result : null);
        }
    }

    public ValueTask<IReadOnlyCollection<UiSemanticExtractionResult>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<UiSemanticExtractionResult>>(_results.Values.ToArray());
        }
    }

    public ValueTask<UiSemanticExtractionResult> UpdateAsync(SessionId sessionId, UiSemanticExtractionResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _results[sessionId] = result;
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _results.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }
}
