using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Tests.Common;

public sealed class NoOpObservabilityRecorder : IObservabilityRecorder
{
    public ValueTask RecordActivityAsync(SessionId sessionId, string stage, string outcome, TimeSpan duration, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordPolicyEvaluationAsync(SessionId sessionId, string policyName, IReadOnlyList<PolicyEvaluationResult> policyResults, bool isPolicyPaused, TimeSpan duration, string outcome, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordDecisionPlanAsync(DecisionPlan plan, TimeSpan duration, string outcome, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordDecisionExecutionAsync(DecisionPlanExecutionResult executionResult, TimeSpan duration, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordCommandExecutionAsync(UiCommand command, UiCommandResult result, TimeSpan duration, string? adapterName, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordAttachmentAsync(SessionId sessionId, string operation, string adapterName, string outcome, TimeSpan duration, string? targetKind, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordPersistenceAsync(SessionId sessionId, string operation, string outcome, TimeSpan duration, string? path, int? itemCount, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordAdapterErrorAsync(SessionId sessionId, string adapterName, string operation, Exception exception, string? reasonCode, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RecordDecisionReasonAsync(SessionId sessionId, string category, string reasonCode, string? reason, string? sourceComponent, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public SessionObservabilitySnapshot? GetSnapshot(SessionId sessionId) => null;

    public SessionObservabilityMetricsSnapshot? GetMetrics(SessionId sessionId) => null;

    public GlobalObservabilitySnapshot GetGlobalSnapshot() => new(DateTimeOffset.UtcNow, SessionObservabilityStatus.Idle, 0, 0, 0, 0, 0, [], []);
}