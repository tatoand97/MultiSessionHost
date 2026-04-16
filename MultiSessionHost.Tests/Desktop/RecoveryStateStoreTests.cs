using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class RecoveryStateStoreTests
{
    [Fact]
    public async Task RepeatedFailures_EnterBackoff_AndSuccessClearsPressure()
    {
        var sessionId = new SessionId("recovery-backoff");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value), clock);

        await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.AttachmentEnsureFailed, "attach", "attach-failed", "first", null, CancellationToken.None);
        var backoff = await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.AttachmentEnsureFailed, "attach", "attach-failed", "second", null, CancellationToken.None);

        Assert.Equal(SessionRecoveryStatus.Backoff, backoff.RecoveryStatus);
        Assert.Equal(SessionRecoveryCircuitState.Closed, backoff.CircuitBreakerState);
        Assert.True(backoff.IsBlockedFromRecoveryAttempts);
        Assert.NotNull(backoff.NextRecoveryAttemptAtUtc);

        var blocked = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.AttachmentEnsure, CancellationToken.None);
        Assert.False(blocked.CanAttempt);
        Assert.Equal("recovery.backoff.active", blocked.ReasonCode);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        var retry = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.AttachmentEnsure, CancellationToken.None);
        Assert.True(retry.CanAttempt);
        Assert.False(retry.IsProbe);
        Assert.Equal(SessionRecoveryStatus.Recovering, retry.Snapshot.RecoveryStatus);

        var healthy = await store.RegisterSuccessAsync(sessionId, "attachment.ensure", "recovery.success_cleared_failures", "ok", null, CancellationToken.None);
        Assert.Equal(SessionRecoveryStatus.Healthy, healthy.RecoveryStatus);
        Assert.Equal(0, healthy.ConsecutiveFailureCount);
        Assert.Null(healthy.NextRecoveryAttemptAtUtc);
    }

    [Fact]
    public async Task CircuitBreaker_Opens_AllowsHalfOpenProbe_AndClosesOnSuccess()
    {
        var sessionId = new SessionId("recovery-circuit");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value, circuitThreshold: 2, backoffThreshold: 10), clock);

        await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.RefreshProjectionFailure, "refresh", "refresh-failed", "first", null, CancellationToken.None);
        var open = await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.RefreshProjectionFailure, "refresh", "refresh-failed", "second", null, CancellationToken.None);

        Assert.Equal(SessionRecoveryStatus.CircuitOpen, open.RecoveryStatus);
        Assert.Equal(SessionRecoveryCircuitState.Open, open.CircuitBreakerState);

        var blocked = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.Refresh, CancellationToken.None);
        Assert.False(blocked.CanAttempt);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        var probe = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.Refresh, CancellationToken.None);
        Assert.True(probe.CanAttempt);
        Assert.True(probe.IsProbe);
        Assert.Equal(SessionRecoveryStatus.HalfOpen, probe.Snapshot.RecoveryStatus);

        var closed = await store.RegisterSuccessAsync(sessionId, "refresh", "recovery.success_cleared_failures", "probe succeeded", null, CancellationToken.None);
        Assert.Equal(SessionRecoveryStatus.Healthy, closed.RecoveryStatus);
        Assert.Equal(SessionRecoveryCircuitState.Closed, closed.CircuitBreakerState);
        Assert.Equal(0, closed.HalfOpenProbeAttempts);
    }

    [Fact]
    public async Task HalfOpenFailure_ReopensCircuit()
    {
        var sessionId = new SessionId("recovery-half-open-fail");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value, circuitThreshold: 2, backoffThreshold: 10), clock);

        await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.SnapshotCaptureFailed, "capture", "capture-failed", "first", null, CancellationToken.None);
        await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.SnapshotCaptureFailed, "capture", "capture-failed", "second", null, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        var probe = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.SnapshotCapture, CancellationToken.None);

        Assert.True(probe.IsProbe);

        var reopened = await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.SnapshotCaptureFailed, "capture", "capture-failed", "probe failed", null, CancellationToken.None);

        Assert.Equal(SessionRecoveryStatus.CircuitOpen, reopened.RecoveryStatus);
        Assert.Equal(SessionRecoveryCircuitState.Open, reopened.CircuitBreakerState);
        Assert.True(reopened.IsBlockedFromRecoveryAttempts);
    }

    [Fact]
    public async Task StaleSnapshot_AndAttachmentInvalidation_AreInspectable()
    {
        var sessionId = new SessionId("recovery-stale");
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value), new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z")));

        var stale = await store.MarkSnapshotStaleAsync(sessionId, "recovery.snapshot.stale_detected", "stale", null, CancellationToken.None);
        var invalid = await store.MarkAttachmentInvalidAsync(sessionId, "recovery.attachment.invalidated", "lost", null, CancellationToken.None);

        Assert.True(stale.IsSnapshotStale);
        Assert.True(invalid.IsAttachmentInvalid);
        Assert.Equal(SessionRecoveryStatus.Recovering, invalid.RecoveryStatus);
    }

    [Fact]
    public async Task InvalidTarget_Quarantines_AndCanBeCleared()
    {
        var sessionId = new SessionId("recovery-quarantine");
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value), new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z")));

        var quarantined = await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.TargetInvalid, "resolve", "recovery.target.quarantined", "missing", null, CancellationToken.None);

        Assert.Equal(SessionRecoveryStatus.Quarantined, quarantined.RecoveryStatus);
        Assert.True(quarantined.IsTargetQuarantined);
        Assert.True(quarantined.IsBlockedFromRecoveryAttempts);

        var blocked = await store.TryBeginAttemptAsync(sessionId, SessionRecoveryAttemptKind.AttachmentEnsure, CancellationToken.None);
        Assert.False(blocked.CanAttempt);

        var cleared = await store.ClearQuarantineAsync(sessionId, "recovery.target.quarantine_cleared", "binding corrected", null, CancellationToken.None);
        Assert.False(cleared.IsTargetQuarantined);
        Assert.Equal(SessionRecoveryStatus.Recovering, cleared.RecoveryStatus);
    }

    [Fact]
    public async Task RepeatedMetadataDrift_EscalatesToQuarantine()
    {
        var sessionId = new SessionId("recovery-drift");
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value, metadataDriftThreshold: 2), new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z")));

        await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.MetadataDrift, "drift", "recovery.metadata_drift.detected", "first", null, CancellationToken.None);
        var quarantined = await store.RegisterFailureAsync(sessionId, SessionRecoveryFailureCategory.MetadataDrift, "drift", "recovery.metadata_drift.detected", "second", null, CancellationToken.None);

        Assert.True(quarantined.MetadataDriftDetected);
        Assert.True(quarantined.IsTargetQuarantined);
        Assert.Equal(SessionRecoveryStatus.Quarantined, quarantined.RecoveryStatus);
    }

    [Fact]
    public async Task AdapterHealth_DistinguishesDegradedFromExhausted()
    {
        var sessionId = new SessionId("recovery-adapter");
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value), new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z")));

        var degraded = await store.MarkAdapterHealthAsync(sessionId, SessionAdapterHealthState.Degraded, "recovery.adapter.degraded", "partial", null, CancellationToken.None);
        var exhausted = await store.MarkAdapterHealthAsync(sessionId, SessionAdapterHealthState.Exhausted, "recovery.adapter.exhausted", "done", null, CancellationToken.None);

        Assert.Equal(SessionAdapterHealthState.Degraded, degraded.AdapterHealthState);
        Assert.Equal(SessionRecoveryStatus.Recovering, degraded.RecoveryStatus);
        Assert.Equal(SessionAdapterHealthState.Exhausted, exhausted.AdapterHealthState);
        Assert.Equal(SessionRecoveryStatus.Exhausted, exhausted.RecoveryStatus);
        Assert.True(exhausted.IsBlockedFromRecoveryAttempts);
    }

    [Fact]
    public async Task Restore_RehydratesSnapshotAndBoundedHistory()
    {
        var sessionId = new SessionId("recovery-restore");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionRecoveryStateStore(CreateOptions(sessionId.Value, historyLimit: 2), clock);
        var snapshot = SessionRecoverySnapshot.Create(sessionId) with
        {
            RecoveryStatus = SessionRecoveryStatus.Backoff,
            ConsecutiveFailureCount = 3,
            NextRecoveryAttemptAtUtc = clock.UtcNow.AddSeconds(1)
        };
        var history = Enumerable.Range(0, 3)
            .Select(index => new SessionRecoveryHistoryEntry(sessionId, clock.UtcNow.AddSeconds(index), $"event-{index}", SessionRecoveryStatus.Recovering, SessionRecoveryCircuitState.Closed, SessionAdapterHealthState.Healthy, null, $"reason-{index}", null, new Dictionary<string, string>()))
            .ToArray();

        await store.RestoreAsync(sessionId, snapshot, history, CancellationToken.None);

        var restored = await store.GetAsync(sessionId, CancellationToken.None);
        var restoredHistory = await store.GetHistoryAsync(sessionId, CancellationToken.None);

        Assert.Equal(SessionRecoveryStatus.Backoff, restored.RecoveryStatus);
        Assert.Equal(3, restored.ConsecutiveFailureCount);
        Assert.Equal(2, restoredHistory.Count);
        Assert.Equal("event-1", restoredHistory[0].Action);
    }

    private static SessionHostOptions CreateOptions(
        string sessionId,
        int backoffThreshold = 2,
        int circuitThreshold = 5,
        int metadataDriftThreshold = 3,
        int historyLimit = 100) =>
        new()
        {
            Sessions = [TestOptionsFactory.Session(sessionId)],
            Recovery = new RecoveryOptions
            {
                ConsecutiveFailureThresholdBeforeBackoff = backoffThreshold,
                InitialBackoffMs = 250,
                MaxBackoffMs = 1_000,
                BackoffMultiplier = 2,
                CircuitBreakerFailureThreshold = circuitThreshold,
                CircuitBreakerOpenDurationMs = 500,
                HalfOpenMaxProbeAttempts = 1,
                SnapshotStaleAfterMs = 1_000,
                ConsecutiveMetadataDriftThreshold = metadataDriftThreshold,
                RecoveryHistoryLimit = historyLimit
            }
        };
}
