using System.Collections.Concurrent;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Observability;

public sealed class InMemorySessionObservabilityStore : ISessionObservabilityStore
{
    private sealed class SessionState
    {
        public readonly object Gate = new();
        public List<SessionObservabilityEvent> Events { get; } = [];
        public List<AdapterErrorRecord> Errors { get; } = [];
        public Dictionary<string, SessionReasonMetric> ReasonMetrics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public SessionObservabilitySummary Summary { get; set; } = SessionObservabilitySummary.Create(new SessionId("observability"), DateTimeOffset.UtcNow);
        public SessionObservabilityMetricsSnapshot? Metrics { get; set; }
    }

    private readonly ConcurrentDictionary<SessionId, SessionState> _sessions = new();
    private readonly int _maxEventsPerSession;
    private readonly int _maxErrorsPerSession;
    private readonly int _maxReasonMetricsPerSession;

    public InMemorySessionObservabilityStore(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxEventsPerSession = options.Observability.MaxEventsPerSession;
        _maxErrorsPerSession = options.Observability.MaxErrorsPerSession;
        _maxReasonMetricsPerSession = options.Observability.MaxReasonMetricsPerSession;
    }

    public ValueTask<SessionObservabilitySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return ValueTask.FromResult<SessionObservabilitySnapshot?>(null);
        }

        lock (state.Gate)
        {
            return ValueTask.FromResult<SessionObservabilitySnapshot?>(CreateSnapshot(sessionId, state));
        }
    }

    public ValueTask<SessionObservabilityMetricsSnapshot?> GetMetricsAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return ValueTask.FromResult<SessionObservabilityMetricsSnapshot?>(null);
        }

        lock (state.Gate)
        {
            return ValueTask.FromResult<SessionObservabilityMetricsSnapshot?>(CreateMetricsSnapshot(sessionId, state));
        }
    }

    public ValueTask<IReadOnlyCollection<SessionObservabilitySnapshot>> GetAllAsync(CancellationToken cancellationToken)
    {
        var snapshots = _sessions
            .OrderBy(pair => pair.Key.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => CreateSnapshot(pair.Key, pair.Value))
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyCollection<SessionObservabilitySnapshot>>(snapshots);
    }

    public ValueTask<IReadOnlyCollection<SessionObservabilitySummary>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        var summaries = _sessions
            .OrderBy(pair => pair.Key.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value.Summary)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyCollection<SessionObservabilitySummary>>(summaries);
    }

    public ValueTask<IReadOnlyCollection<SessionObservabilityEvent>> GetEventsAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionObservabilityEvent>>([]);
        }

        lock (state.Gate)
        {
            return ValueTask.FromResult<IReadOnlyCollection<SessionObservabilityEvent>>(state.Events.ToArray());
        }
    }

    public ValueTask<IReadOnlyList<AdapterErrorRecord>> GetErrorsAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return ValueTask.FromResult<IReadOnlyList<AdapterErrorRecord>>([]);
        }

        lock (state.Gate)
        {
            return ValueTask.FromResult<IReadOnlyList<AdapterErrorRecord>>(state.Errors.ToArray());
        }
    }

    public ValueTask<GlobalObservabilitySnapshot> GetGlobalSnapshotAsync(CancellationToken cancellationToken)
    {
        var summaries = _sessions.Values.Select(state => state.Summary).ToArray();
        var recentErrors = _sessions.Values.SelectMany(state => state.Errors).OrderByDescending(error => error.OccurredAtUtc).Take(50).ToArray();
        var activeSessions = summaries.Count(summary => summary.SnapshotCount > 0 || summary.ExtractionCount > 0 || summary.PolicyEvaluationCount > 0 || summary.DecisionExecutionCount > 0 || summary.CommandExecutionCount > 0);
        var faultedSessions = summaries.Count(summary => summary.Status == SessionObservabilityStatus.Faulted);
        var pausedSessions = summaries.Count(summary => summary.Status == SessionObservabilityStatus.Paused);
        var totalEvents = summaries.Sum(summary => summary.SnapshotCount + summary.ExtractionCount + summary.DomainProjectionCount + summary.PolicyEvaluationCount + summary.DecisionExecutionCount + summary.CommandExecutionCount + summary.PersistenceFlushCount + summary.PersistenceRehydrateCount + summary.AttachCount + summary.ReattachCount + summary.AdapterErrorCount);
        var status = faultedSessions > 0 ? SessionObservabilityStatus.Degraded : pausedSessions > 0 ? SessionObservabilityStatus.Paused : summaries.Length > 0 ? SessionObservabilityStatus.Healthy : SessionObservabilityStatus.Idle;

        return ValueTask.FromResult(new GlobalObservabilitySnapshot(DateTimeOffset.UtcNow, status, activeSessions, faultedSessions, pausedSessions, totalEvents, recentErrors.Length, summaries, recentErrors));
    }

    public ValueTask RecordAsync(SessionObservabilityEvent sessionEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);

        var state = _sessions.GetOrAdd(sessionEvent.SessionId, _ => new SessionState { Summary = SessionObservabilitySummary.Create(sessionEvent.SessionId, sessionEvent.OccurredAtUtc) });
        lock (state.Gate)
        {
            if (state.Summary.SessionId != sessionEvent.SessionId)
            {
                state.Summary = SessionObservabilitySummary.Create(sessionEvent.SessionId, sessionEvent.OccurredAtUtc);
            }

            state.Events.Add(sessionEvent);
            if (state.Events.Count > _maxEventsPerSession)
            {
                state.Events.RemoveRange(0, state.Events.Count - _maxEventsPerSession);
            }

            state.Summary = UpdateSummary(state.Summary, sessionEvent, state.ReasonMetrics, sessionEvent.OccurredAtUtc);
            state.Metrics = CreateMetricsSnapshot(sessionEvent.SessionId, state);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordErrorAsync(AdapterErrorRecord errorRecord, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errorRecord);

        var state = _sessions.GetOrAdd(errorRecord.SessionId, _ => new SessionState { Summary = SessionObservabilitySummary.Create(errorRecord.SessionId, errorRecord.OccurredAtUtc) });
        lock (state.Gate)
        {
            if (state.Summary.SessionId != errorRecord.SessionId)
            {
                state.Summary = SessionObservabilitySummary.Create(errorRecord.SessionId, errorRecord.OccurredAtUtc);
            }

            state.Errors.Add(errorRecord);
            if (state.Errors.Count > _maxErrorsPerSession)
            {
                state.Errors.RemoveRange(0, state.Errors.Count - _maxErrorsPerSession);
            }

            state.Summary = UpdateSummary(state.Summary, errorRecord, state.ReasonMetrics, errorRecord.OccurredAtUtc);
            state.Metrics = CreateMetricsSnapshot(errorRecord.SessionId, state);
        }

        return ValueTask.CompletedTask;
    }

    private static SessionObservabilitySnapshot? CreateSnapshot(SessionId sessionId, SessionState state)
    {
        lock (state.Gate)
        {
            if (state.Summary.SessionId != sessionId)
            {
                return null;
            }

            return new SessionObservabilitySnapshot(
                state.Summary,
                state.Events.ToArray(),
                state.Metrics ?? CreateMetricsSnapshot(sessionId, state),
                state.Errors.ToArray());
        }
    }

    private static SessionObservabilityMetricsSnapshot CreateMetricsSnapshot(SessionId sessionId, SessionState state)
    {
        var reasonMetrics = state.ReasonMetrics.Values
            .OrderByDescending(metric => metric.Count)
            .ThenBy(metric => metric.ReasonCode, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray();

        var recentLatencies = state.Events.OfType<SessionLatencyMeasurement>().ToArray();

        return new SessionObservabilityMetricsSnapshot(sessionId, DateTimeOffset.UtcNow, state.Summary, recentLatencies, reasonMetrics, state.Errors.ToArray());
    }

    private static SessionObservabilitySummary UpdateSummary(
        SessionObservabilitySummary summary,
        SessionObservabilityEvent sessionEvent,
        Dictionary<string, SessionReasonMetric> reasonMetrics,
        DateTimeOffset now)
    {
        var reasonCounts = new Dictionary<string, long>(summary.ReasonCounts, StringComparer.OrdinalIgnoreCase);

        void IncrementReason(string category, string? reasonCode, string? reason, string? sourceComponent)
        {
            if (string.IsNullOrWhiteSpace(reasonCode))
            {
                return;
            }

            var key = $"{category}:{reasonCode}";
            reasonCounts[key] = reasonCounts.TryGetValue(key, out var existing) ? existing + 1 : 1;
            reasonMetrics[key] = reasonMetrics.TryGetValue(key, out var metric)
                ? metric with { Count = metric.Count + 1, LastOccurredAtUtc = now, Reason = reason ?? metric.Reason, SourceComponent = sourceComponent ?? metric.SourceComponent }
                : new SessionReasonMetric(summary.SessionId, category, reasonCode, reason, 1, now, sourceComponent);

            if (reasonMetrics.Count > 200)
            {
                var overflow = reasonMetrics.Values
                    .OrderBy(metric => metric.Count)
                    .ThenBy(metric => metric.LastOccurredAtUtc)
                    .Take(reasonMetrics.Count - 200)
                    .Select(metric => $"{metric.Category}:{metric.ReasonCode}")
                    .ToArray();

                foreach (var keyToRemove in overflow)
                {
                    reasonMetrics.Remove(keyToRemove);
                    reasonCounts.Remove(keyToRemove);
                }
            }
        }

        if (sessionEvent is SessionLatencyMeasurement latency)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                LastReasonCode = latency.ReasonCode ?? summary.LastReasonCode,
                LastReason = latency.Reason ?? summary.LastReason
            };

            switch (latency.Category)
            {
                case nameof(SessionObservabilityCategory.Snapshot):
                    summary = summary with { SnapshotCount = summary.SnapshotCount + 1, LastSnapshotDurationMs = latency.DurationMs, LastSnapshotOutcome = latency.Outcome };
                    break;
                case nameof(SessionObservabilityCategory.Extraction):
                    summary = summary with { ExtractionCount = summary.ExtractionCount + 1, LastExtractionDurationMs = latency.DurationMs, LastExtractionOutcome = latency.Outcome };
                    break;
                case nameof(SessionObservabilityCategory.Domain):
                    summary = summary with { DomainProjectionCount = summary.DomainProjectionCount + 1 };
                    break;
                case nameof(SessionObservabilityCategory.Policy):
                    summary = summary with { PolicyEvaluationCount = summary.PolicyEvaluationCount + 1, LastPolicyEvaluationDurationMs = latency.DurationMs, LastPolicyOutcome = latency.Outcome };
                    break;
                case nameof(SessionObservabilityCategory.Execution):
                    summary = summary with { DecisionExecutionCount = summary.DecisionExecutionCount + 1, LastDecisionExecutionDurationMs = latency.DurationMs, LastDecisionOutcome = latency.Outcome };
                    break;
                case nameof(SessionObservabilityCategory.Command):
                    summary = summary with { CommandExecutionCount = summary.CommandExecutionCount + 1, LastCommandDurationMs = latency.DurationMs, LastCommandOutcome = latency.Outcome };
                    break;
                case nameof(SessionObservabilityCategory.Persistence):
                    if (latency.EventType.Contains("rehydrate", StringComparison.OrdinalIgnoreCase))
                    {
                        summary = summary with { PersistenceRehydrateCount = summary.PersistenceRehydrateCount + 1, LastPersistenceRehydrateDurationMs = latency.DurationMs, LastRehydrateOutcome = latency.Outcome };
                    }
                    else
                    {
                        summary = summary with { PersistenceFlushCount = summary.PersistenceFlushCount + 1, LastPersistenceFlushDurationMs = latency.DurationMs, LastPersistenceOutcome = latency.Outcome };
                    }
                    break;
                case nameof(SessionObservabilityCategory.Attachment):
                    if (latency.EventType.Contains("reattach", StringComparison.OrdinalIgnoreCase))
                    {
                        summary = summary with { ReattachCount = summary.ReattachCount + 1, LastReattachDurationMs = latency.DurationMs, LastReattachOutcome = latency.Outcome };
                    }
                    else
                    {
                        summary = summary with { AttachCount = summary.AttachCount + 1, LastAttachDurationMs = latency.DurationMs, LastAttachOutcome = latency.Outcome };
                    }
                    break;
                case nameof(SessionObservabilityCategory.Activity):
                    if (latency.Outcome == SessionObservabilityOutcome.Withdrawn.ToString())
                    {
                        summary = summary with { WithdrawCount = summary.WithdrawCount + 1, Status = SessionObservabilityStatus.Paused };
                    }
                    else if (latency.Outcome == SessionObservabilityOutcome.Aborted.ToString())
                    {
                        summary = summary with { AbortCount = summary.AbortCount + 1, Status = SessionObservabilityStatus.Faulted };
                    }
                    else if (latency.Outcome == SessionObservabilityOutcome.Hidden.ToString())
                    {
                        summary = summary with { HideCount = summary.HideCount + 1, Status = SessionObservabilityStatus.Paused };
                    }
                    else if (latency.Outcome == SessionObservabilityOutcome.Waiting.ToString())
                    {
                        summary = summary with { WaitCount = summary.WaitCount + 1 };
                    }
                    break;
            }

            if (!string.IsNullOrWhiteSpace(latency.ReasonCode))
            {
                IncrementReason(latency.Category, latency.ReasonCode, latency.Reason, latency.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is PolicyEvaluationEvent policyEvent)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                PolicyEvaluationCount = summary.PolicyEvaluationCount + 1,
                LastPolicyEvaluationDurationMs = policyEvent.DurationMs,
                LastPolicyOutcome = policyEvent.Outcome,
                Status = policyEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? SessionObservabilityStatus.Degraded : summary.Status,
                LastReasonCode = policyEvent.ReasonCode ?? summary.LastReasonCode,
                LastReason = policyEvent.Reason ?? summary.LastReason
            };

            if (policyEvent.WasPolicyPaused)
            {
                summary = summary with { Status = SessionObservabilityStatus.Paused };
            }

            if (!string.IsNullOrWhiteSpace(policyEvent.ReasonCode))
            {
                IncrementReason(SessionObservabilityCategory.Policy.ToString(), policyEvent.ReasonCode, policyEvent.Reason, policyEvent.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is DecisionExecutionEvent decisionEvent)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                DecisionExecutionCount = summary.DecisionExecutionCount + 1,
                LastDecisionExecutionDurationMs = decisionEvent.DurationMs,
                LastDecisionOutcome = decisionEvent.ExecutionStatus,
                Status = decisionEvent.ExecutionStatus == SessionObservabilityOutcome.Failure.ToString() ? SessionObservabilityStatus.Degraded : summary.Status,
                LastReasonCode = decisionEvent.ReasonCode ?? summary.LastReasonCode,
                LastReason = decisionEvent.Reason ?? summary.LastReason
            };

            if (decisionEvent.ExecutionStatus.Equals(SessionObservabilityOutcome.Withdrawn.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { WithdrawCount = summary.WithdrawCount + 1, Status = SessionObservabilityStatus.Paused };
            }
            else if (decisionEvent.ExecutionStatus.Equals(SessionObservabilityOutcome.Aborted.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { AbortCount = summary.AbortCount + 1, Status = SessionObservabilityStatus.Faulted };
            }
            else if (decisionEvent.ExecutionStatus.Equals(SessionObservabilityOutcome.Hidden.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { HideCount = summary.HideCount + 1, Status = SessionObservabilityStatus.Paused };
            }
            else if (decisionEvent.ExecutionStatus.Equals(SessionObservabilityOutcome.Waiting.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { WaitCount = summary.WaitCount + 1 };
            }

            if (!string.IsNullOrWhiteSpace(decisionEvent.ReasonCode))
            {
                IncrementReason(SessionObservabilityCategory.Execution.ToString(), decisionEvent.ReasonCode, decisionEvent.Reason, decisionEvent.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is CommandExecutionEvent commandEvent)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                CommandExecutionCount = summary.CommandExecutionCount + 1,
                LastCommandDurationMs = commandEvent.DurationMs,
                LastCommandOutcome = commandEvent.Outcome,
                Status = commandEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? SessionObservabilityStatus.Degraded : summary.Status,
                LastReasonCode = commandEvent.ReasonCode ?? summary.LastReasonCode,
                LastReason = commandEvent.Reason ?? summary.LastReason,
                CommandFailureCount = commandEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? summary.CommandFailureCount + 1 : summary.CommandFailureCount,
                RecentErrorCount = commandEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? summary.RecentErrorCount + 1 : summary.RecentErrorCount
            };

            if (!string.IsNullOrWhiteSpace(commandEvent.ReasonCode))
            {
                IncrementReason(SessionObservabilityCategory.Command.ToString(), commandEvent.ReasonCode, commandEvent.Reason, commandEvent.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is AttachmentLifecycleEvent attachmentEvent)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                Status = attachmentEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? SessionObservabilityStatus.Degraded : summary.Status,
                LastReasonCode = attachmentEvent.ReasonCode ?? summary.LastReasonCode,
                LastReason = attachmentEvent.Reason ?? summary.LastReason
            };

            if (attachmentEvent.Operation.Contains("reattach", StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { ReattachCount = summary.ReattachCount + 1, LastReattachDurationMs = attachmentEvent.DurationMs, LastReattachOutcome = attachmentEvent.Outcome };
            }
            else
            {
                summary = summary with { AttachCount = summary.AttachCount + 1, LastAttachDurationMs = attachmentEvent.DurationMs, LastAttachOutcome = attachmentEvent.Outcome };
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is PersistenceLifecycleEvent persistenceEvent)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                Status = persistenceEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? SessionObservabilityStatus.Degraded : summary.Status,
                LastPersistenceError = persistenceEvent.Outcome == SessionObservabilityOutcome.Failure.ToString() ? persistenceEvent.Reason ?? summary.LastPersistenceError : summary.LastPersistenceError,
                LastReasonCode = persistenceEvent.ReasonCode ?? summary.LastReasonCode,
                LastReason = persistenceEvent.Reason ?? summary.LastReason
            };

            if (persistenceEvent.Operation.Contains("rehydrate", StringComparison.OrdinalIgnoreCase))
            {
                summary = summary with { PersistenceRehydrateCount = summary.PersistenceRehydrateCount + 1, LastPersistenceRehydrateDurationMs = persistenceEvent.DurationMs, LastRehydrateOutcome = persistenceEvent.Outcome };
            }
            else
            {
                summary = summary with { PersistenceFlushCount = summary.PersistenceFlushCount + 1, LastPersistenceFlushDurationMs = persistenceEvent.DurationMs, LastPersistenceOutcome = persistenceEvent.Outcome };
            }

            if (persistenceEvent.Outcome == SessionObservabilityOutcome.Failure.ToString())
            {
                summary = summary with { PersistenceFailureCount = summary.PersistenceFailureCount + 1, RecentErrorCount = summary.RecentErrorCount + 1 };
            }

            if (!string.IsNullOrWhiteSpace(persistenceEvent.ReasonCode))
            {
                IncrementReason(SessionObservabilityCategory.Persistence.ToString(), persistenceEvent.ReasonCode, persistenceEvent.Reason, persistenceEvent.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        if (sessionEvent is AdapterErrorRecord adapterError)
        {
            summary = summary with
            {
                LastUpdatedAtUtc = now,
                AdapterErrorCount = summary.AdapterErrorCount + 1,
                RecentErrorCount = summary.RecentErrorCount + 1,
                LastAdapterError = $"{adapterError.AdapterName}:{adapterError.Operation}:{adapterError.Message}",
                Status = SessionObservabilityStatus.Degraded,
                LastReasonCode = adapterError.ReasonCode ?? summary.LastReasonCode,
                LastReason = adapterError.Message
            };

            if (!string.IsNullOrWhiteSpace(adapterError.ReasonCode))
            {
                IncrementReason(SessionObservabilityCategory.Adapter.ToString(), adapterError.ReasonCode, adapterError.Message, adapterError.SourceComponent);
            }

            return summary with { ReasonCounts = reasonCounts };
        }

        return summary with { LastUpdatedAtUtc = now, ReasonCounts = reasonCounts };
    }
}