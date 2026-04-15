using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// In-memory, thread-safe implementation of ISessionActivityStateStore.
/// Stores activity snapshots and history per session with bounded history retention.
/// </summary>
public sealed class InMemorySessionActivityStateStore : ISessionActivityStateStore
{
    private const int MaxHistoryPerSession = 1000;

    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionActivitySnapshot> _snapshots = [];

    public ValueTask InitializeAsync(SessionId sessionId, SessionActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_snapshots.ContainsKey(sessionId))
            {
                throw new InvalidOperationException($"Activity state for session '{sessionId}' is already initialized.");
            }

            _snapshots[sessionId] = snapshot;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<SessionActivitySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_snapshots.TryGetValue(sessionId, out var snapshot) ? snapshot : null);
        }
    }

    public ValueTask<SessionActivitySnapshot> UpsertAsync(SessionId sessionId, SessionActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _snapshots[sessionId] = snapshot;
            return ValueTask.FromResult(snapshot);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionActivitySnapshot>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionActivitySnapshot>>(_snapshots.Values.ToArray());
        }
    }

    public ValueTask<IReadOnlyList<SessionActivityHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_snapshots.TryGetValue(sessionId, out var snapshot))
            {
                return ValueTask.FromResult<IReadOnlyList<SessionActivityHistoryEntry>>(Array.Empty<SessionActivityHistoryEntry>());
            }

            return ValueTask.FromResult(snapshot.History);
        }
    }

    public ValueTask RestoreAsync(SessionId sessionId, SessionActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _snapshots[sessionId] = snapshot with
            {
                History = snapshot.History
                    .OrderBy(static entry => entry.OccurredAtUtc)
                    .TakeLast(MaxHistoryPerSession)
                    .ToArray()
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _snapshots.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Creates a new snapshot with updated history, preserving history limit.
    /// </summary>
    public static SessionActivitySnapshot AppendTransition(
        SessionActivitySnapshot current,
        SessionActivityTransition transition)
    {
        var history = new List<SessionActivityHistoryEntry>(current.History)
        {
            new(transition.FromState, transition.ToState, transition.ReasonCode, transition.Reason, transition.OccurredAtUtc, transition.Metadata)
        };

        // Trim history if it exceeds max size (keep most recent)
        if (history.Count > MaxHistoryPerSession)
        {
            history = history.Skip(history.Count - MaxHistoryPerSession).ToList();
        }

        return current with
        {
            PreviousState = current.CurrentState,
            CurrentState = transition.ToState,
            LastTransitionAtUtc = transition.OccurredAtUtc,
            LastReasonCode = transition.ReasonCode,
            LastReason = transition.Reason,
            LastMetadata = transition.Metadata,
            History = history.AsReadOnly()
        };
    }
}
