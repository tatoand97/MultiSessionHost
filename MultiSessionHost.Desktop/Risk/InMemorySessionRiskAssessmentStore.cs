using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Risk;

public sealed class InMemorySessionRiskAssessmentStore : ISessionRiskAssessmentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, RiskAssessmentResult> _results = [];

    public ValueTask InitializeAsync(SessionId sessionId, RiskAssessmentResult result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_results.ContainsKey(sessionId))
            {
                throw new InvalidOperationException($"Risk assessment result for session '{sessionId}' is already initialized.");
            }

            _results[sessionId] = result;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<RiskAssessmentResult?> GetLatestAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_results.TryGetValue(sessionId, out var result) ? result : null);
        }
    }

    public ValueTask<IReadOnlyCollection<RiskAssessmentResult>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<RiskAssessmentResult>>(_results.Values.ToArray());
        }
    }

    public ValueTask<RiskAssessmentResult> UpdateAsync(SessionId sessionId, RiskAssessmentResult result, CancellationToken cancellationToken)
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
