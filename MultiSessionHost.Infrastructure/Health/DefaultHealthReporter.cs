using System.Collections.Concurrent;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Health;

public sealed class DefaultHealthReporter : IHealthReporter
{
    private readonly ConcurrentDictionary<SessionId, SessionMetricsCounter> _metrics = new();

    public void RecordRegistration(SessionDefinition definition)
    {
        _metrics.TryAdd(definition.Id, new SessionMetricsCounter());
    }

    public void RecordTransition(SessionId sessionId, SessionStatus fromStatus, SessionStatus toStatus)
    {
        _metrics.TryAdd(sessionId, new SessionMetricsCounter());
    }

    public void RecordHeartbeat(SessionHeartbeat heartbeat)
    {
        Interlocked.Increment(ref _metrics.GetOrAdd(heartbeat.SessionId, static _ => new SessionMetricsCounter()).HeartbeatsEmitted);
    }

    public void RecordTick(SessionId sessionId)
    {
        Interlocked.Increment(ref _metrics.GetOrAdd(sessionId, static _ => new SessionMetricsCounter()).TicksExecuted);
    }

    public void RecordError(SessionId sessionId, Exception exception)
    {
        Interlocked.Increment(ref _metrics.GetOrAdd(sessionId, static _ => new SessionMetricsCounter()).Errors);
    }

    public void RecordRetry(SessionId sessionId)
    {
        Interlocked.Increment(ref _metrics.GetOrAdd(sessionId, static _ => new SessionMetricsCounter()).Retries);
    }

    public ProcessHealthSnapshot CreateSnapshot(IReadOnlyCollection<SessionSnapshot> sessions, DateTimeOffset generatedAtUtc)
    {
        var sessionHealth = sessions
            .Select(
                snapshot =>
                {
                    var metrics = _metrics.GetOrAdd(snapshot.SessionId, static _ => new SessionMetricsCounter());
                    return new SessionHealthSnapshot(
                        snapshot.SessionId,
                        snapshot.Definition.DisplayName,
                        snapshot.Runtime.CurrentStatus,
                        snapshot.Runtime.DesiredStatus,
                        snapshot.Runtime.ObservedStatus,
                        snapshot.Runtime.LastHeartbeatUtc,
                        snapshot.Runtime.LastError,
                        snapshot.Runtime.RetryPolicy.IsCircuitOpen,
                        new SessionMetricsSnapshot(
                            Interlocked.Read(ref metrics.TicksExecuted),
                            Interlocked.Read(ref metrics.Errors),
                            Interlocked.Read(ref metrics.Retries),
                            Interlocked.Read(ref metrics.HeartbeatsEmitted)));
                })
            .ToArray();

        return new ProcessHealthSnapshot(
            generatedAtUtc,
            sessionHealth.Count(session => session.CurrentStatus is SessionStatus.Starting or SessionStatus.Running or SessionStatus.Paused),
            sessionHealth.Count(session => session.CurrentStatus == SessionStatus.Faulted),
            sessionHealth.Sum(session => session.Metrics.TicksExecuted),
            sessionHealth.Sum(session => session.Metrics.Errors),
            sessionHealth.Sum(session => session.Metrics.Retries),
            sessionHealth.Sum(session => session.Metrics.HeartbeatsEmitted),
            sessionHealth);
    }

    private sealed class SessionMetricsCounter
    {
        public long TicksExecuted;
        public long Errors;
        public long Retries;
        public long HeartbeatsEmitted;
    }
}
