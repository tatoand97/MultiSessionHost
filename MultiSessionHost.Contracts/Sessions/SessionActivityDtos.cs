namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionActivityTransitionDto(
    string FromState,
    string ToState,
    string ReasonCode,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionActivityHistoryEntryDto(
    string FromState,
    string ToState,
    string ReasonCode,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SessionActivitySnapshotDto(
    string SessionId,
    string CurrentState,
    string? PreviousState,
    DateTimeOffset? LastTransitionAtUtc,
    string? LastReasonCode,
    string? LastReason,
    IReadOnlyDictionary<string, string> LastMetadata,
    IReadOnlyList<SessionActivityHistoryEntryDto> History,
    bool IsTerminal);

public sealed record SessionActivityHistoryDto(
    string SessionId,
    IReadOnlyList<SessionActivityHistoryEntryDto> Entries);
