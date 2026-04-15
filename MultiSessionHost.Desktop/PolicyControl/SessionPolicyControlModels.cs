using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.PolicyControl;

public enum SessionPolicyControlAction
{
    PausePolicy = 1,
    ResumePolicy = 2
}

public sealed record SessionPolicyControlState(
    SessionId SessionId,
    bool IsPolicyPaused,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? ResumedAtUtc,
    DateTimeOffset? LastChangedAtUtc,
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static SessionPolicyControlState Create(SessionId sessionId) =>
        new(
            sessionId,
            IsPolicyPaused: false,
            PausedAtUtc: null,
            ResumedAtUtc: null,
            LastChangedAtUtc: null,
            ReasonCode: null,
            Reason: null,
            ChangedBy: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed record SessionPolicyControlHistoryEntry(
    SessionId SessionId,
    SessionPolicyControlAction Action,
    DateTimeOffset OccurredAtUtc,
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PolicyControlActionRequest(
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PolicyControlActionResult(
    SessionPolicyControlState State,
    SessionPolicyControlAction Action,
    bool WasChanged,
    IReadOnlyList<SessionPolicyControlHistoryEntry> History,
    string? Message);

public sealed record PolicyEvaluationGateResult(
    SessionId SessionId,
    bool IsPolicyPaused,
    SessionPolicyControlState State,
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);