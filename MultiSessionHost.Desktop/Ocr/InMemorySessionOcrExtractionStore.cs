using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Ocr;

public sealed class InMemorySessionOcrExtractionStore : ISessionOcrExtractionStore
{
    private sealed class SessionOcrState
    {
        public SessionOcrExtractionResult? Latest { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionOcrState> _stateBySessionId = new();

    public ValueTask<SessionOcrExtractionResult> UpsertLatestAsync(SessionId sessionId, SessionOcrExtractionResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stateBySessionId.TryGetValue(sessionId, out var state))
            {
                state = new SessionOcrState();
                _stateBySessionId[sessionId] = state;
            }

            state.Latest = result;
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SessionOcrExtractionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionOcrExtractionResult>> GetAllLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionOcrExtractionResult>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest)
                    .Where(static result => result is not null)
                    .Select(static result => result!)
                    .OrderBy(static result => result.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask<SessionOcrExtractionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest?.ToSummary() : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionOcrExtractionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionOcrExtractionSummary>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest?.ToSummary())
                    .Where(static summary => summary is not null)
                    .Select(static summary => summary!)
                    .OrderBy(static summary => summary.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }
}
