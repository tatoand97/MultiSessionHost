using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.Observability;

public sealed class DefaultObservabilityRecorder : IObservabilityRecorder
{
    private readonly SessionHostOptions _options;
    private readonly ISessionObservabilityStore _store;
    private readonly IClock _clock;
    private readonly ILogger<DefaultObservabilityRecorder> _logger;

    public DefaultObservabilityRecorder(
        SessionHostOptions options,
        ISessionObservabilityStore store,
        IClock clock,
        ILogger<DefaultObservabilityRecorder> logger)
    {
        _options = options;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public ValueTask RecordActivityAsync(
        SessionId sessionId,
        string stage,
        string outcome,
        TimeSpan duration,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        RecordLatencyAsync(sessionId, stage, SessionObservabilityCategory.Activity, outcome, duration, reasonCode, reason, sourceComponent, metadata, cancellationToken);

    public ValueTask RecordPolicyEvaluationAsync(
        SessionId sessionId,
        string policyName,
        IReadOnlyList<PolicyEvaluationResult> policyResults,
        bool isPolicyPaused,
        TimeSpan duration,
        string outcome,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var eventMetadata = BuildMetadata(metadata, ("policyCount", policyResults.Count.ToString()), ("policyPaused", isPolicyPaused.ToString()));
        return new ValueTask(RecordPolicyEvaluationInternalAsync(sessionId, policyName, policyResults, isPolicyPaused, duration, outcome, reasonCode, reason, sourceComponent, eventMetadata, cancellationToken));
    }

    public async ValueTask RecordDecisionPlanAsync(
        DecisionPlan plan,
        TimeSpan duration,
        string outcome,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("directiveCount", plan.Directives.Count.ToString()), ("planStatus", plan.PlanStatus.ToString()));
        var eventRecord = new SessionLatencyMeasurement(
            plan.SessionId,
            Guid.NewGuid(),
            "decision.plan",
            SessionObservabilityCategory.Policy.ToString(),
            _clock.UtcNow,
            duration.TotalMilliseconds,
            outcome,
            reasonCode,
            reason,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            RuntimeObservability.PolicyEvaluationsTotal.Add(1, new KeyValuePair<string, object?>("session.id", plan.SessionId.Value));
            RuntimeObservability.PolicyEvaluationDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", plan.SessionId.Value));
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
        await RecordDecisionReasonMetricsAsync(plan.SessionId, plan, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordDecisionExecutionAsync(
        DecisionPlanExecutionResult executionResult,
        TimeSpan duration,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("directiveCount", executionResult.Summary.TotalDirectives.ToString()), ("executionStatus", executionResult.ExecutionStatus.ToString()));
        var eventRecord = new DecisionExecutionEvent(
            executionResult.SessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            executionResult.PlanFingerprint,
            executionResult.ExecutionStatus.ToString(),
            duration.TotalMilliseconds,
            executionResult.WasAutoExecuted,
            executionResult.Summary.TotalDirectives,
            executionResult.Summary.SucceededCount,
            executionResult.Summary.FailedCount,
            executionResult.Summary.SkippedCount,
            executionResult.Summary.DeferredCount,
            executionResult.Summary.BlockedCount,
            executionResult.Summary.AbortedCount,
            executionResult.FailureReason is null ? null : "execution-failure",
            executionResult.FailureReason,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            RuntimeObservability.DecisionExecutionsTotal.Add(1, new KeyValuePair<string, object?>("session.id", executionResult.SessionId.Value));
            RuntimeObservability.DecisionExecutionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", executionResult.SessionId.Value));
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
        await RecordExecutionReasonMetricsAsync(executionResult.SessionId, executionResult, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordCommandExecutionAsync(
        UiCommand command,
        UiCommandResult result,
        TimeSpan duration,
        string? adapterName,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || !_options.Observability.IncludeVerboseCommandEvents)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("commandKind", command.Kind.ToString()), ("nodeId", command.NodeId?.Value ?? string.Empty), ("adapterName", adapterName ?? string.Empty));
        var eventRecord = new CommandExecutionEvent(
            command.SessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            command.Kind.ToString(),
            command.NodeId?.Value,
            adapterName,
            result.Succeeded ? SessionObservabilityOutcome.Success.ToString() : SessionObservabilityOutcome.Failure.ToString(),
            duration.TotalMilliseconds,
            result.FailureCode,
            result.Message,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            RuntimeObservability.CommandExecutionsTotal.Add(1, new KeyValuePair<string, object?>("session.id", command.SessionId.Value));
            RuntimeObservability.CommandExecutionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", command.SessionId.Value));
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await RecordReasonMetricAsync(command.SessionId, SessionObservabilityCategory.Command.ToString(), result.FailureCode ?? "command-failure", result.Message, sourceComponent, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask RecordAttachmentAsync(
        SessionId sessionId,
        string operation,
        string adapterName,
        string outcome,
        TimeSpan duration,
        string? targetKind,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || !_options.Observability.IncludeAttachmentEvents)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("operation", operation), ("adapterName", adapterName), ("targetKind", targetKind ?? string.Empty));
        var eventRecord = new AttachmentLifecycleEvent(
            sessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            operation,
            adapterName,
            targetKind,
            outcome,
            duration.TotalMilliseconds,
            reasonCode,
            reason,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            if (operation.Contains("reattach", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeObservability.AttachmentsReattachTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
            }
            else
            {
                RuntimeObservability.AttachmentsAttachTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
            }

            RuntimeObservability.AttachmentResolveDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordPersistenceAsync(
        SessionId sessionId,
        string operation,
        string outcome,
        TimeSpan duration,
        string? path,
        int? itemCount,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || !_options.Observability.IncludePersistenceEvents)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("operation", operation), ("path", path ?? string.Empty), ("itemCount", itemCount?.ToString() ?? string.Empty));
        var eventRecord = new PersistenceLifecycleEvent(
            sessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            operation,
            outcome,
            duration.TotalMilliseconds,
            path,
            itemCount,
            reasonCode,
            reason,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            if (operation.Contains("rehydrate", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeObservability.PersistenceRehydrateTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                RuntimeObservability.PersistenceRehydrateDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
            }
            else
            {
                RuntimeObservability.PersistenceFlushTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                RuntimeObservability.PersistenceFlushDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
            }
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RecordAdapterErrorAsync(
        SessionId sessionId,
        string adapterName,
        string operation,
        Exception exception,
        string? reasonCode,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var eventMetadata = BuildMetadata(metadata, ("adapterName", adapterName), ("operation", operation), ("exceptionType", exception.GetType().FullName ?? exception.GetType().Name));
        var errorRecord = new AdapterErrorRecord(
            sessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            adapterName,
            operation,
            exception.GetType().Name,
            exception.Message,
            reasonCode,
            sourceComponent,
            null,
            null,
            eventMetadata);

        if (_options.Observability.EnableMetrics)
        {
            RuntimeObservability.AdapterErrorsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
        }

        await _store.RecordErrorAsync(errorRecord, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RecordDecisionReasonAsync(
        SessionId sessionId,
        string category,
        string reasonCode,
        string? reason,
        string? sourceComponent,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return ValueTask.CompletedTask;
        }

        return _store.RecordAsync(
            new SessionLatencyMeasurement(
                sessionId,
                Guid.NewGuid(),
                $"{category}.reason",
                category,
                _clock.UtcNow,
                0,
                SessionObservabilityOutcome.Success.ToString(),
                reasonCode,
                reason,
                sourceComponent,
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["reasonCode"] = reasonCode }),
            cancellationToken);
    }

    public SessionObservabilitySnapshot? GetSnapshot(SessionId sessionId) =>
        _store.GetAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();

    public SessionObservabilityMetricsSnapshot? GetMetrics(SessionId sessionId) =>
        _store.GetMetricsAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();

    public GlobalObservabilitySnapshot GetGlobalSnapshot() =>
        _store.GetGlobalSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();

    private async ValueTask RecordLatencyAsync(
        SessionId sessionId,
        string stage,
        SessionObservabilityCategory category,
        string outcome,
        TimeSpan duration,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var eventRecord = new SessionLatencyMeasurement(
            sessionId,
            Guid.NewGuid(),
            stage,
            category.ToString(),
            _clock.UtcNow,
            duration.TotalMilliseconds,
            outcome,
            reasonCode,
            reason,
            sourceComponent,
            null,
            null,
            metadata is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(metadata, StringComparer.Ordinal));

        if (_options.Observability.EnableMetrics)
        {
            switch (category)
            {
                case SessionObservabilityCategory.Snapshot:
                    RuntimeObservability.UiSnapshotsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    RuntimeObservability.UiSnapshotDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    break;
                case SessionObservabilityCategory.Extraction:
                    RuntimeObservability.SemanticExtractionsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    RuntimeObservability.SemanticExtractionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    break;
                case SessionObservabilityCategory.Domain:
                    RuntimeObservability.DomainProjectionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    break;
                case SessionObservabilityCategory.Command:
                    RuntimeObservability.CommandExecutionsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    RuntimeObservability.CommandExecutionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    break;
                case SessionObservabilityCategory.Persistence:
                    RuntimeObservability.PersistenceFlushDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    break;
            }
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordPolicyEvaluationInternalAsync(
        SessionId sessionId,
        string policyName,
        IReadOnlyList<PolicyEvaluationResult> policyResults,
        bool isPolicyPaused,
        TimeSpan duration,
        string outcome,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        var eventRecord = new PolicyEvaluationEvent(
            sessionId,
            Guid.NewGuid(),
            _clock.UtcNow,
            policyName,
            outcome,
            duration.TotalMilliseconds,
            policyResults.Sum(static result => result.Directives.Count),
            policyResults.Count,
            policyResults.Any(static result => result.Reasons.Count > 0),
            isPolicyPaused,
            reasonCode,
            reason,
            sourceComponent,
            null,
            null,
            metadata);

        if (_options.Observability.EnableMetrics)
        {
            RuntimeObservability.PolicyEvaluationsTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
            RuntimeObservability.PolicyEvaluationDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", sessionId.Value));
        }

        await _store.RecordAsync(eventRecord, cancellationToken).ConfigureAwait(false);
        await RecordReasonMetricAsync(sessionId, SessionObservabilityCategory.Policy.ToString(), reasonCode ?? outcome, reason, sourceComponent, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordDecisionReasonMetricsAsync(SessionId sessionId, DecisionPlan plan, CancellationToken cancellationToken)
    {
        foreach (var directive in plan.Directives)
        {
            switch (directive.DirectiveKind)
            {
                case DecisionDirectiveKind.Withdraw:
                    RuntimeObservability.DecisionsWithdrawTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    await RecordReasonMetricAsync(sessionId, "withdraw", directive.Reasons.FirstOrDefault()?.Code ?? "withdraw", directive.Reasons.FirstOrDefault()?.Message, nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
                    break;
                case DecisionDirectiveKind.Abort:
                    RuntimeObservability.DecisionsAbortTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    await RecordReasonMetricAsync(sessionId, "abort", directive.Reasons.FirstOrDefault()?.Code ?? "abort", directive.Reasons.FirstOrDefault()?.Message, nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
                    break;
                case DecisionDirectiveKind.PauseActivity:
                    RuntimeObservability.DecisionsHideTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    await RecordReasonMetricAsync(sessionId, "hide", directive.Reasons.FirstOrDefault()?.Code ?? "hide", directive.Reasons.FirstOrDefault()?.Message, nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
                    break;
                case DecisionDirectiveKind.Wait:
                    RuntimeObservability.DecisionsWaitTotal.Add(1, new KeyValuePair<string, object?>("session.id", sessionId.Value));
                    await RecordReasonMetricAsync(sessionId, "wait", directive.Reasons.FirstOrDefault()?.Code ?? "wait", directive.Reasons.FirstOrDefault()?.Message, nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task RecordExecutionReasonMetricsAsync(SessionId sessionId, DecisionPlanExecutionResult executionResult, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(executionResult.FailureReason))
        {
            await RecordReasonMetricAsync(sessionId, "execution", executionResult.Metadata.TryGetValue("skipReason", out var skipReason) ? skipReason : "execution-failure", executionResult.FailureReason, nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
        }

        if (executionResult.ExecutionStatus == DecisionPlanExecutionStatus.Skipped)
        {
            await RecordReasonMetricAsync(sessionId, "execution", executionResult.Metadata.TryGetValue("skipReason", out var reasonCode) ? reasonCode : "skipped", executionResult.FailureReason ?? "Decision execution skipped.", nameof(DefaultObservabilityRecorder), cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask RecordReasonMetricAsync(
        SessionId sessionId,
        string category,
        string reasonCode,
        string? reason,
        string? sourceComponent,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return ValueTask.CompletedTask;
        }

        return _store.RecordAsync(
            new SessionLatencyMeasurement(
                sessionId,
                Guid.NewGuid(),
                $"{category}.reason",
                category,
                _clock.UtcNow,
                0,
                SessionObservabilityOutcome.Success.ToString(),
                reasonCode,
                reason,
                sourceComponent,
                null,
                null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["reasonCode"] = reasonCode }),
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(IReadOnlyDictionary<string, string>? metadata, params (string Key, string Value)[] values)
    {
        var result = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private bool IsEnabled => _options.Observability.EnableObservability;
}