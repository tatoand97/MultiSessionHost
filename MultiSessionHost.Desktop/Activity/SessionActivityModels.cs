using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// Represents a single state transition with timing and reason information.
/// </summary>
public sealed record SessionActivityTransition(
    SessionActivityStateKind FromState,
    SessionActivityStateKind ToState,
    string ReasonCode,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Represents a historical entry of a state transition for audit and inspection purposes.
/// </summary>
public sealed record SessionActivityHistoryEntry(
    SessionActivityStateKind FromState,
    SessionActivityStateKind ToState,
    string ReasonCode,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Represents the current activity state snapshot for a session, including history.
/// </summary>
public sealed record SessionActivitySnapshot(
    SessionId SessionId,
    SessionActivityStateKind CurrentState,
    SessionActivityStateKind? PreviousState,
    DateTimeOffset? LastTransitionAtUtc,
    string? LastReasonCode,
    string? LastReason,
    IReadOnlyDictionary<string, string> LastMetadata,
    IReadOnlyList<SessionActivityHistoryEntry> History)
{
    public bool IsTerminal => CurrentState == SessionActivityStateKind.Faulted;

    public static SessionActivitySnapshot CreateBootstrap(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            SessionActivityStateKind.Idle,
            PreviousState: null,
            LastTransitionAtUtc: now,
            LastReasonCode: "bootstrap-idle",
            LastReason: "Session initialized in idle state",
            LastMetadata: new Dictionary<string, string>(),
            History: []);
}

/// <summary>
/// Context provided to the activity state evaluator to determine state transitions.
/// Includes all signals needed for deterministic state evaluation.
/// </summary>
public sealed record SessionActivityEvaluationContext(
    SessionId SessionId,
    SessionDomainState DomainState,
    DecisionPlan DecisionPlan,
    RiskAssessmentResult? RiskAssessment,
    SessionActivitySnapshot? PreviousSnapshot,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Result of activity state evaluation, including new state snapshot and any transition that occurred.
/// </summary>
public sealed record SessionActivityEvaluationResult(
    SessionActivitySnapshot NewSnapshot,
    SessionActivityTransition? Transition,
    string EvaluationReasonCode,
    string EvaluationReason);
