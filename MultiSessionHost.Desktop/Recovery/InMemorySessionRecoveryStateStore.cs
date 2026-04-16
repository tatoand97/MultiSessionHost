using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Observability;

namespace MultiSessionHost.Desktop.Recovery;

public sealed class InMemorySessionRecoveryStateStore : ISessionRecoveryStateStore
{
    private sealed class SessionRecoveryStateHolder
    {
        public SessionRecoverySnapshot Current { get; set; } = null!;

        public List<SessionRecoveryHistoryEntry> History { get; } = [];
    }

    private readonly object _gate = new();
    private readonly SessionHostOptions _options;
    private readonly IClock _clock;
    private readonly Dictionary<SessionId, SessionRecoveryStateHolder> _states = [];

    public InMemorySessionRecoveryStateStore(SessionHostOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public ValueTask<SessionRecoverySnapshot> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(GetOrCreateStateUnsafe(sessionId).Current);
        }
    }

    public ValueTask<IReadOnlyCollection<SessionRecoverySnapshot>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionRecoverySnapshot>>(
                _states.Values.Select(static holder => holder.Current).ToArray());
        }
    }

    public ValueTask<IReadOnlyList<SessionRecoveryHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<SessionRecoveryHistoryEntry>>(
                _states.TryGetValue(sessionId, out var holder) ? holder.History.ToArray() : []);
        }
    }

    public ValueTask<SessionRecoveryAttemptDecision> TryBeginAttemptAsync(
        SessionId sessionId,
        SessionRecoveryAttemptKind attemptKind,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var holder = GetOrCreateStateUnsafe(sessionId);
            var now = _clock.UtcNow;
            var snapshot = holder.Current;

            if (!IsEnabled || snapshot.RecoveryStatus is SessionRecoveryStatus.Healthy or SessionRecoveryStatus.Recovering)
            {
                RuntimeObservability.RecoveryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));

                return ValueTask.FromResult(new SessionRecoveryAttemptDecision(snapshot, true, snapshot.CircuitBreakerState == SessionRecoveryCircuitState.HalfOpen, null, null, snapshot.NextRecoveryAttemptAtUtc));
            }

            if (snapshot.IsTargetQuarantined || snapshot.RecoveryStatus is SessionRecoveryStatus.Quarantined or SessionRecoveryStatus.Exhausted or SessionRecoveryStatus.Faulted)
            {
                return ValueTask.FromResult(BlockedDecision(snapshot, "recovery-target-quarantined", "Recovery is blocked because the target is quarantined or exhausted."));
            }

            if (snapshot.NextRecoveryAttemptAtUtc is not null && now < snapshot.NextRecoveryAttemptAtUtc.Value)
            {
                var blockedSnapshot = snapshot with
                {
                    RecoveryStatus = SessionRecoveryStatus.Backoff,
                    IsBlockedFromRecoveryAttempts = true,
                    LastTransitionAtUtc = snapshot.LastTransitionAtUtc ?? now
                };
                holder.Current = blockedSnapshot;
                AppendHistory(holder, blockedSnapshot, $"skip:{attemptKind}", null, "recovery.backoff.skipped_attempt", "Recovery attempt skipped because backoff has not elapsed.", null);
                RuntimeObservability.RecoveryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                RuntimeObservability.RecoveryFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                return ValueTask.FromResult(BlockedDecision(blockedSnapshot, "recovery.backoff.active", "Recovery attempt skipped because backoff has not elapsed."));
            }

            if (snapshot.RecoveryStatus == SessionRecoveryStatus.Backoff && snapshot.CircuitBreakerState == SessionRecoveryCircuitState.Closed)
            {
                var transition = snapshot with
                {
                    RecoveryStatus = SessionRecoveryStatus.Recovering,
                    IsBlockedFromRecoveryAttempts = false,
                    LastTransitionAtUtc = now,
                    LastRecoveryAction = $"retry:{attemptKind}",
                    LastRecoveryReasonCode = "recovery-backoff-elapsed",
                    LastRecoveryReason = "Recovery backoff elapsed; retry allowed."
                };
                holder.Current = transition;
                AppendHistory(holder, transition, transition.LastRecoveryAction, null, transition.LastRecoveryReasonCode, transition.LastRecoveryReason, null);
                RuntimeObservability.RecoveryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                return ValueTask.FromResult(new SessionRecoveryAttemptDecision(transition, true, false, null, null, transition.NextRecoveryAttemptAtUtc));
            }

            if (snapshot.CircuitBreakerState == SessionRecoveryCircuitState.Open)
            {
                var halfOpenAttempts = snapshot.HalfOpenProbeAttempts + 1;
                var canProbe = halfOpenAttempts <= Math.Max(1, _options.Recovery.HalfOpenMaxProbeAttempts);
                var transition = snapshot with
                {
                    RecoveryStatus = canProbe ? SessionRecoveryStatus.HalfOpen : SessionRecoveryStatus.CircuitOpen,
                    CircuitBreakerState = canProbe ? SessionRecoveryCircuitState.HalfOpen : SessionRecoveryCircuitState.Open,
                    HalfOpenProbeAttempts = canProbe ? halfOpenAttempts : snapshot.HalfOpenProbeAttempts,
                    IsBlockedFromRecoveryAttempts = !canProbe,
                    LastTransitionAtUtc = now,
                    NextRecoveryAttemptAtUtc = canProbe ? now : snapshot.NextRecoveryAttemptAtUtc,
                    LastRecoveryAction = canProbe ? $"probe:{attemptKind}" : "circuit-open",
                    LastRecoveryReasonCode = canProbe ? "recovery-circuit-half-open" : "recovery-circuit-open",
                    LastRecoveryReason = canProbe ? "Half-open probe attempt allowed." : "Recovery attempt blocked while the circuit is open."
                };
                holder.Current = transition;
                AppendHistory(holder, transition, transition.LastRecoveryAction ?? string.Empty, null, transition.LastRecoveryReasonCode, transition.LastRecoveryReason, null);

                RuntimeObservability.RecoveryAttemptsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));

                if (!canProbe)
                {
                    RuntimeObservability.RecoveryFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    return ValueTask.FromResult(BlockedDecision(transition, "recovery.circuit.open", "Recovery attempt blocked while the circuit is open."));
                }

                return ValueTask.FromResult(new SessionRecoveryAttemptDecision(transition, true, true, "recovery.circuit.half_open", "Half-open probe attempt allowed.", transition.NextRecoveryAttemptAtUtc));
            }

            return ValueTask.FromResult(new SessionRecoveryAttemptDecision(snapshot, true, false, null, null, snapshot.NextRecoveryAttemptAtUtc));
        }
    }

    public ValueTask<SessionRecoverySnapshot> RegisterSuccessAsync(
        SessionId sessionId,
        string action,
        string? reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, action, reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Healthy,
            CircuitBreakerState = SessionRecoveryCircuitState.Closed,
            ConsecutiveFailureCount = 0,
            LastSuccessAtUtc = now,
            BackoffUntilUtc = null,
            NextRecoveryAttemptAtUtc = null,
            IsSnapshotStale = false,
            IsAttachmentInvalid = false,
            MetadataDriftDetected = false,
            AdapterHealthState = SessionAdapterHealthState.Healthy,
            LastRecoveryAction = action,
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now,
            IsBlockedFromRecoveryAttempts = false,
            HalfOpenProbeAttempts = 0
        }, cancellationToken);

    

    public ValueTask<SessionRecoverySnapshot> RegisterFailureAsync(
        SessionId sessionId,
        SessionRecoveryFailureCategory category,
        string action,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, action, reasonCode, reason, metadata, (snapshot, now) => ApplyFailure(snapshot, category, action, reasonCode, reason, now), cancellationToken, category);

    public ValueTask<SessionRecoverySnapshot> MarkSnapshotStaleAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "snapshot-stale", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Recovering,
            IsSnapshotStale = true,
            IsBlockedFromRecoveryAttempts = false,
            LastRecoveryAction = "snapshot-stale",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, SessionRecoveryFailureCategory.SnapshotStale);

    public ValueTask<SessionRecoverySnapshot> MarkSnapshotInvalidatedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "snapshot-invalidated", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Recovering,
            IsSnapshotStale = true,
            LastRecoveryAction = "snapshot-invalidated",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, SessionRecoveryFailureCategory.SnapshotStale);

    public ValueTask<SessionRecoverySnapshot> MarkAttachmentInvalidAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "attachment-invalidated", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Recovering,
            IsAttachmentInvalid = true,
            LastRecoveryAction = "attachment-invalidated",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, SessionRecoveryFailureCategory.AttachmentLost);

    public ValueTask<SessionRecoverySnapshot> MarkTargetQuarantinedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "target-quarantined", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Quarantined,
            CircuitBreakerState = SessionRecoveryCircuitState.Open,
            IsTargetQuarantined = true,
            IsBlockedFromRecoveryAttempts = true,
            TargetQuarantineReasonCode = reasonCode,
            LastRecoveryAction = "target-quarantined",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, SessionRecoveryFailureCategory.TargetInvalid);

    public ValueTask<SessionRecoverySnapshot> ClearQuarantineAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "target-quarantine-cleared", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Recovering,
            CircuitBreakerState = SessionRecoveryCircuitState.Closed,
            IsTargetQuarantined = false,
            TargetQuarantineReasonCode = null,
            IsBlockedFromRecoveryAttempts = false,
            LastRecoveryAction = "target-quarantine-cleared",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken);

    public ValueTask<SessionRecoverySnapshot> MarkMetadataDriftDetectedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "metadata-drift-detected", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Recovering,
            MetadataDriftDetected = true,
            LastRecoveryAction = "metadata-drift-detected",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, SessionRecoveryFailureCategory.MetadataDrift);

    public ValueTask<SessionRecoverySnapshot> MarkMetadataDriftClearedAsync(
        SessionId sessionId,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, "metadata-drift-cleared", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = SessionRecoveryStatus.Healthy,
            MetadataDriftDetected = false,
            LastRecoveryAction = "metadata-drift-cleared",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken);

    public ValueTask<SessionRecoverySnapshot> MarkAdapterHealthAsync(
        SessionId sessionId,
        SessionAdapterHealthState healthState,
        string reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        UpdateAsync(sessionId, $"adapter-health:{healthState}", reasonCode, reason, metadata, (snapshot, now) => snapshot with
        {
            RecoveryStatus = healthState == SessionAdapterHealthState.Exhausted ? SessionRecoveryStatus.Exhausted : SessionRecoveryStatus.Recovering,
            AdapterHealthState = healthState,
            IsBlockedFromRecoveryAttempts = healthState == SessionAdapterHealthState.Exhausted,
            CircuitBreakerState = healthState == SessionAdapterHealthState.Exhausted ? SessionRecoveryCircuitState.Open : snapshot.CircuitBreakerState,
            LastRecoveryAction = $"adapter-health:{healthState}",
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now
        }, cancellationToken, healthState == SessionAdapterHealthState.Exhausted ? SessionRecoveryFailureCategory.AdapterExhausted : SessionRecoveryFailureCategory.AdapterDegraded);

    public ValueTask RestoreAsync(
        SessionId sessionId,
        SessionRecoverySnapshot? snapshot,
        IReadOnlyList<SessionRecoveryHistoryEntry> history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        lock (_gate)
        {
            var holder = GetOrCreateStateUnsafe(sessionId);
            holder.Current = snapshot is null
                ? SessionRecoverySnapshot.Create(sessionId)
                : snapshot with
                {
                    SessionId = sessionId,
                    Metadata = CopyDictionary(snapshot.Metadata),
                    FailureCountsByCategory = new Dictionary<SessionRecoveryFailureCategory, int>(snapshot.FailureCountsByCategory)
                };

            holder.History.Clear();
            holder.History.AddRange(history
                .Where(entry => entry.SessionId == sessionId)
                .OrderBy(static entry => entry.OccurredAtUtc)
                .TakeLast(_options.Recovery.RecoveryHistoryLimit)
                .Select(CloneHistoryEntry));
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

    private bool IsEnabled => _options.Recovery.EnableRecovery;

    private ValueTask<SessionRecoverySnapshot> UpdateAsync(
        SessionId sessionId,
        string action,
        string? reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata,
        Func<SessionRecoverySnapshot, DateTimeOffset, SessionRecoverySnapshot> update,
        CancellationToken cancellationToken,
        SessionRecoveryFailureCategory? category = null)
    {
        lock (_gate)
        {
            var holder = GetOrCreateStateUnsafe(sessionId);
            var now = _clock.UtcNow;
            var updated = update(holder.Current, now);
            updated = Normalize(updated, now);
            holder.Current = updated;
            AppendHistory(holder, updated, action, category, reasonCode, reason, metadata);
            RecordMetrics(action, updated, category, metadata, now);
            return ValueTask.FromResult(updated);
        }
    }

    private SessionRecoverySnapshot ApplyFailure(
        SessionRecoverySnapshot snapshot,
        SessionRecoveryFailureCategory category,
        string action,
        string reasonCode,
        string? reason,
        DateTimeOffset now)
    {
        var failureCounts = new Dictionary<SessionRecoveryFailureCategory, int>(snapshot.FailureCountsByCategory);
        failureCounts.TryGetValue(category, out var categoryCount);
        categoryCount++;
        failureCounts[category] = categoryCount;

        var consecutiveFailures = snapshot.ConsecutiveFailureCount + 1;
        var nextSnapshot = snapshot with
        {
            FailureCountsByCategory = failureCounts,
            ConsecutiveFailureCount = consecutiveFailures,
            LastFailureAtUtc = now,
            LastRecoveryAction = action,
            LastRecoveryReasonCode = reasonCode,
            LastRecoveryReason = reason,
            LastTransitionAtUtc = now,
            IsAttachmentInvalid = category is SessionRecoveryFailureCategory.AttachmentEnsureFailed or SessionRecoveryFailureCategory.AttachmentLost,
            MetadataDriftDetected = category == SessionRecoveryFailureCategory.MetadataDrift || snapshot.MetadataDriftDetected
        };

        if (category == SessionRecoveryFailureCategory.TargetInvalid)
        {
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = SessionRecoveryStatus.Quarantined,
                CircuitBreakerState = SessionRecoveryCircuitState.Open,
                IsTargetQuarantined = true,
                TargetQuarantineReasonCode = reasonCode,
                IsBlockedFromRecoveryAttempts = true,
                NextRecoveryAttemptAtUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs),
                BackoffUntilUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs)
            };
        }
        else if (category == SessionRecoveryFailureCategory.MetadataDrift && categoryCount >= Math.Max(1, _options.Recovery.ConsecutiveMetadataDriftThreshold))
        {
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = SessionRecoveryStatus.Quarantined,
                CircuitBreakerState = SessionRecoveryCircuitState.Open,
                IsTargetQuarantined = true,
                TargetQuarantineReasonCode = reasonCode,
                IsBlockedFromRecoveryAttempts = true,
                NextRecoveryAttemptAtUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs),
                BackoffUntilUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs)
            };
        }
        else if (category is SessionRecoveryFailureCategory.AdapterExhausted || categoryCount >= _options.Recovery.ExhaustedAdapterFailureThreshold)
        {
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = SessionRecoveryStatus.Exhausted,
                CircuitBreakerState = SessionRecoveryCircuitState.Open,
                AdapterHealthState = SessionAdapterHealthState.Exhausted,
                IsBlockedFromRecoveryAttempts = true,
                NextRecoveryAttemptAtUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs),
                BackoffUntilUtc = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs)
            };
        }
        else if (consecutiveFailures >= _options.Recovery.CircuitBreakerFailureThreshold)
        {
            var openUntil = now.AddMilliseconds(_options.Recovery.CircuitBreakerOpenDurationMs);
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = SessionRecoveryStatus.CircuitOpen,
                CircuitBreakerState = SessionRecoveryCircuitState.Open,
                IsBlockedFromRecoveryAttempts = true,
                HalfOpenProbeAttempts = 0,
                BackoffUntilUtc = openUntil,
                NextRecoveryAttemptAtUtc = openUntil
            };
            RuntimeObservability.RecoveryCircuitOpenTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
        }
        else if (consecutiveFailures >= _options.Recovery.ConsecutiveFailureThresholdBeforeBackoff)
        {
            var delay = ComputeBackoff(consecutiveFailures);
            var nextEligible = now.Add(delay);
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = SessionRecoveryStatus.Backoff,
                CircuitBreakerState = SessionRecoveryCircuitState.Closed,
                IsBlockedFromRecoveryAttempts = true,
                BackoffUntilUtc = nextEligible,
                NextRecoveryAttemptAtUtc = nextEligible,
                AdapterHealthState = category is SessionRecoveryFailureCategory.AdapterTransientFailure or SessionRecoveryFailureCategory.AdapterDegraded
                    ? SessionAdapterHealthState.Degraded
                    : nextSnapshot.AdapterHealthState
            };
            RuntimeObservability.RecoveryBackoffDuration.Record(delay.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));
        }
        else
        {
            nextSnapshot = nextSnapshot with
            {
                RecoveryStatus = category is SessionRecoveryFailureCategory.AdapterTransientFailure or SessionRecoveryFailureCategory.AdapterDegraded
                    ? SessionRecoveryStatus.Recovering
                    : snapshot.RecoveryStatus == SessionRecoveryStatus.Quarantined
                        ? SessionRecoveryStatus.Quarantined
                        : SessionRecoveryStatus.Recovering,
                CircuitBreakerState = snapshot.CircuitBreakerState,
                IsBlockedFromRecoveryAttempts = snapshot.IsTargetQuarantined,
                AdapterHealthState = category is SessionRecoveryFailureCategory.AdapterTransientFailure or SessionRecoveryFailureCategory.AdapterDegraded
                    ? SessionAdapterHealthState.Degraded
                    : snapshot.AdapterHealthState
            };
        }

        RuntimeObservability.RecoveryFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", snapshot.SessionId.Value));

        return Normalize(nextSnapshot, now);
    }

    private TimeSpan ComputeBackoff(int consecutiveFailures)
    {
        var backoffThreshold = Math.Max(1, _options.Recovery.ConsecutiveFailureThresholdBeforeBackoff);
        var exponent = Math.Max(0, consecutiveFailures - backoffThreshold);
        var initial = Math.Max(1, _options.Recovery.InitialBackoffMs);
        var multiplier = Math.Max(1d, _options.Recovery.BackoffMultiplier);
        var milliseconds = initial * Math.Pow(multiplier, exponent);
        var capped = Math.Min(milliseconds, _options.Recovery.MaxBackoffMs);
        return TimeSpan.FromMilliseconds(capped);
    }

    private static SessionRecoverySnapshot Normalize(SessionRecoverySnapshot snapshot, DateTimeOffset now)
    {
        var isBlocked = snapshot.IsTargetQuarantined || snapshot.RecoveryStatus is SessionRecoveryStatus.Backoff or SessionRecoveryStatus.CircuitOpen or SessionRecoveryStatus.Quarantined or SessionRecoveryStatus.Exhausted or SessionRecoveryStatus.Faulted;

        if (snapshot.RecoveryStatus == SessionRecoveryStatus.Healthy)
        {
            isBlocked = false;
        }

        return snapshot with { IsBlockedFromRecoveryAttempts = isBlocked, LastTransitionAtUtc = snapshot.LastTransitionAtUtc ?? now };
    }

    private static SessionRecoveryAttemptDecision BlockedDecision(SessionRecoverySnapshot snapshot, string reasonCode, string reason) =>
        new(snapshot, false, false, reasonCode, reason, snapshot.NextRecoveryAttemptAtUtc ?? snapshot.BackoffUntilUtc);

    private void RecordMetrics(
        string action,
        SessionRecoverySnapshot snapshot,
        SessionRecoveryFailureCategory? category,
        IReadOnlyDictionary<string, string>? metadata,
        DateTimeOffset now)
    {
        var sessionId = snapshot.SessionId.Value;

        if (action.Contains("success", StringComparison.OrdinalIgnoreCase) || snapshot.RecoveryStatus == SessionRecoveryStatus.Healthy && category is null)
        {
            RuntimeObservability.RecoverySuccessTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId));
        }

        if (snapshot.RecoveryStatus == SessionRecoveryStatus.Quarantined)
        {
            RuntimeObservability.RecoveryTargetQuarantineTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId));
        }

        if (snapshot.IsSnapshotStale || category == SessionRecoveryFailureCategory.SnapshotStale)
        {
            RuntimeObservability.RecoveryStaleSnapshotTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId));
        }

        if (snapshot.IsBlockedFromRecoveryAttempts && snapshot.RecoveryStatus == SessionRecoveryStatus.CircuitOpen)
        {
            RuntimeObservability.RecoveryCircuitOpenTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId));
        }
    }

    private void AppendHistory(
        SessionRecoveryStateHolder holder,
        SessionRecoverySnapshot snapshot,
        string action,
        SessionRecoveryFailureCategory? failureCategory,
        string? reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string>? metadata)
    {
        holder.History.Add(
            new SessionRecoveryHistoryEntry(
                snapshot.SessionId,
                _clock.UtcNow,
                action,
                snapshot.RecoveryStatus,
                snapshot.CircuitBreakerState,
                snapshot.AdapterHealthState,
                failureCategory,
                reasonCode,
                reason,
                metadata is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(metadata, StringComparer.Ordinal)));

        if (holder.History.Count > _options.Recovery.RecoveryHistoryLimit)
        {
            holder.History.RemoveRange(0, holder.History.Count - _options.Recovery.RecoveryHistoryLimit);
        }
    }

    private SessionRecoveryStateHolder GetOrCreateStateUnsafe(SessionId sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            return state;
        }

        state = new SessionRecoveryStateHolder
        {
            Current = SessionRecoverySnapshot.Create(sessionId)
        };
        _states[sessionId] = state;
        return state;
    }

    private static IReadOnlyDictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string> source) =>
        new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static SessionRecoveryHistoryEntry CloneHistoryEntry(SessionRecoveryHistoryEntry entry) =>
        new(
            entry.SessionId,
            entry.OccurredAtUtc,
            entry.Action,
            entry.RecoveryStatus,
            entry.CircuitBreakerState,
            entry.AdapterHealthState,
            entry.FailureCategory,
            entry.ReasonCode,
            entry.Reason,
            new Dictionary<string, string>(entry.Metadata, StringComparer.Ordinal));
}
