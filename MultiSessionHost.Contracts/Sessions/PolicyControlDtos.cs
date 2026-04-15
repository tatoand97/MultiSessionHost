namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionPolicyControlStateDto(
    string SessionId,
    bool IsPolicyPaused,
    DateTimeOffset? PausedAtUtc,
    DateTimeOffset? ResumedAtUtc,
    DateTimeOffset? LastChangedAtUtc,
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionPolicyControlHistoryEntryDto(
    string SessionId,
    string Action,
    DateTimeOffset OccurredAtUtc,
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PolicyControlActionRequestDto(
    string? ReasonCode,
    string? Reason,
    string? ChangedBy,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record PolicyControlActionResultDto(
    string SessionId,
    string Action,
    bool WasChanged,
    SessionPolicyControlStateDto State,
    IReadOnlyList<SessionPolicyControlHistoryEntryDto> History,
    string? Message);