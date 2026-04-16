using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Templates;

public sealed class InMemorySessionTemplateDetectionStore : ISessionTemplateDetectionStore
{
    private sealed class SessionTemplateState
    {
        public SessionTemplateDetectionResult? Latest { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionTemplateState> _stateBySessionId = new();

    public ValueTask<SessionTemplateDetectionResult> UpsertLatestAsync(SessionId sessionId, SessionTemplateDetectionResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stateBySessionId.TryGetValue(sessionId, out var state))
            {
                state = new SessionTemplateState();
                _stateBySessionId[sessionId] = state;
            }

            state.Latest = result;
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SessionTemplateDetectionResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionTemplateDetectionResult>> GetAllLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionTemplateDetectionResult>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest)
                    .Where(static result => result is not null)
                    .Select(static result => result!)
                    .OrderBy(static result => result.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask<SessionTemplateDetectionSummary?> GetLatestSummaryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_stateBySessionId.TryGetValue(sessionId, out var state) ? state.Latest?.ToSummary() : null);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionTemplateDetectionSummary>> GetAllLatestSummariesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionTemplateDetectionSummary>>(
                _stateBySessionId.Values
                    .Select(static state => state.Latest?.ToSummary())
                    .Where(static summary => summary is not null)
                    .Select(static summary => summary!)
                    .OrderBy(static summary => summary.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }
}
