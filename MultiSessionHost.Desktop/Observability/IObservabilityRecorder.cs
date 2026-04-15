using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.Observability;

public interface IObservabilityRecorder
{
    ValueTask RecordActivityAsync(
        SessionId sessionId,
        string stage,
        string outcome,
        TimeSpan duration,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RecordPolicyEvaluationAsync(
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
        CancellationToken cancellationToken);

    ValueTask RecordDecisionPlanAsync(
        DecisionPlan plan,
        TimeSpan duration,
        string outcome,
        string? reasonCode,
        string? reason,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RecordDecisionExecutionAsync(
        DecisionPlanExecutionResult executionResult,
        TimeSpan duration,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RecordCommandExecutionAsync(
        UiCommand command,
        UiCommandResult result,
        TimeSpan duration,
        string? adapterName,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RecordAttachmentAsync(
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
        CancellationToken cancellationToken);

    ValueTask RecordPersistenceAsync(
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
        CancellationToken cancellationToken);

    ValueTask RecordAdapterErrorAsync(
        SessionId sessionId,
        string adapterName,
        string operation,
        Exception exception,
        string? reasonCode,
        string? sourceComponent,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken);

    ValueTask RecordDecisionReasonAsync(
        SessionId sessionId,
        string category,
        string reasonCode,
        string? reason,
        string? sourceComponent,
        CancellationToken cancellationToken);

    SessionObservabilitySnapshot? GetSnapshot(SessionId sessionId);

    SessionObservabilityMetricsSnapshot? GetMetrics(SessionId sessionId);

    GlobalObservabilitySnapshot GetGlobalSnapshot();
}