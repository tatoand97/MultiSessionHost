using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.PolicyControl;

public sealed class InMemorySessionPolicyControlStore : ISessionPolicyControlStore
{
    private sealed class SessionPolicyControlStateHolder
    {
        public SessionPolicyControlState Current { get; set; } = null!;

        public List<SessionPolicyControlHistoryEntry> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly int _maxHistoryEntries;
    private readonly IClock _clock;
    private readonly Dictionary<SessionId, SessionPolicyControlStateHolder> _states = [];

    public InMemorySessionPolicyControlStore(SessionHostOptions options, IClock clock)
    {
        _maxHistoryEntries = options.PolicyControl.MaxHistoryEntries;
        _clock = clock;
    }

    public ValueTask<SessionPolicyControlState> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(GetOrCreateStateUnsafe(sessionId).Current);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionPolicyControlState>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionPolicyControlState>>(
                _states.Values.Select(static state => state.Current).ToArray());
        }
    }

    public ValueTask<IReadOnlyList<SessionPolicyControlHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<SessionPolicyControlHistoryEntry>>(
                _states.TryGetValue(sessionId, out var state) ? state.History.ToArray() : []);
        }
    }

    public ValueTask<PolicyControlActionResult> PauseAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, request, SessionPolicyControlAction.PausePolicy, cancellationToken);

    public ValueTask<PolicyControlActionResult> ResumeAsync(SessionId sessionId, PolicyControlActionRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, request, SessionPolicyControlAction.ResumePolicy, cancellationToken);

    public ValueTask RestoreAsync(
        SessionId sessionId,
        SessionPolicyControlState? state,
        IReadOnlyList<SessionPolicyControlHistoryEntry> history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        lock (_gate)
        {
            var holder = GetOrCreateStateUnsafe(sessionId);
            holder.Current = state is null
                ? SessionPolicyControlState.Create(sessionId)
                : state with { SessionId = sessionId, Metadata = CopyDictionary(state.Metadata) };
            holder.History.Clear();
            holder.History.AddRange(history
                .Where(entry => entry.SessionId == sessionId)
                .OrderBy(static entry => entry.OccurredAtUtc)
                .TakeLast(_maxHistoryEntries)
                .Select(CloneEntry));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _states.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask<PolicyControlActionResult> UpdateAsync(
        SessionId sessionId,
        PolicyControlActionRequest request,
        SessionPolicyControlAction action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var holder = GetOrCreateStateUnsafe(sessionId);
            var now = _clock.UtcNow;
            var desiredPaused = action == SessionPolicyControlAction.PausePolicy;

            if (holder.Current.IsPolicyPaused == desiredPaused)
            {
                return ValueTask.FromResult(
                    new PolicyControlActionResult(
                        holder.Current,
                        action,
                        WasChanged: false,
                        holder.History.ToArray(),
                        desiredPaused ? "Policy is already paused." : "Policy is already resumed."));
            }

            var state = holder.Current with
            {
                IsPolicyPaused = desiredPaused,
                PausedAtUtc = desiredPaused ? now : holder.Current.PausedAtUtc,
                ResumedAtUtc = desiredPaused ? holder.Current.ResumedAtUtc : now,
                LastChangedAtUtc = now,
                ReasonCode = Normalize(request.ReasonCode),
                Reason = Normalize(request.Reason),
                ChangedBy = Normalize(request.ChangedBy),
                Metadata = CopyDictionary(request.Metadata)
            };

            holder.Current = state;
            holder.History.Add(new SessionPolicyControlHistoryEntry(
                sessionId,
                action,
                now,
                Normalize(request.ReasonCode),
                Normalize(request.Reason),
                Normalize(request.ChangedBy),
                CopyDictionary(request.Metadata)));
            TrimHistory(holder.History);

            return ValueTask.FromResult(
                new PolicyControlActionResult(
                    state,
                    action,
                    WasChanged: true,
                    holder.History.ToArray(),
                    desiredPaused ? "Policy evaluation paused." : "Policy evaluation resumed."));
        }
    }

    private SessionPolicyControlStateHolder GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionPolicyControlStateHolder
        {
            Current = SessionPolicyControlState.Create(sessionId)
        };
        _states[sessionId] = state;
        return state;
    }

    private void TrimHistory(List<SessionPolicyControlHistoryEntry> history)
    {
        if (history.Count > _maxHistoryEntries)
        {
            history.RemoveRange(0, history.Count - _maxHistoryEntries);
        }
    }

    private static SessionPolicyControlHistoryEntry CloneEntry(SessionPolicyControlHistoryEntry entry) =>
        new(
            entry.SessionId,
            entry.Action,
            entry.OccurredAtUtc,
            entry.ReasonCode,
            entry.Reason,
            entry.ChangedBy,
            CopyDictionary(entry.Metadata));

    private static IReadOnlyDictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string> source) =>
        new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}