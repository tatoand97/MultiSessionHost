using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public sealed class InMemorySessionFramePreprocessingStore : ISessionFramePreprocessingStore
{
    private sealed class SessionPreprocessingState
    {
        public SessionFramePreprocessingResult? Latest { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionPreprocessingState> _stateBySessionId = new();

    public ValueTask<SessionFramePreprocessingResult> UpsertLatestAsync(SessionId sessionId, SessionFramePreprocessingResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stateBySessionId.TryGetValue(sessionId, out var state))
            {
                state = new SessionPreprocessingState();
                _stateBySessionId[sessionId] = state;
            }

            state.Latest = result;
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SessionFramePreprocessingResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionFramePreprocessingResult>> GetAllLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionFramePreprocessingResult>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest)
                    .Where(static result => result is not null)
                    .Select(static result => result!)
                    .OrderBy(static result => result.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask<SessionFramePreprocessingSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest?.ToSummary() : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionFramePreprocessingSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionFramePreprocessingSummary>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest?.ToSummary())
                    .Where(static summary => summary is not null)
                    .Select(static summary => summary!)
                    .OrderBy(static summary => summary.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }
}
