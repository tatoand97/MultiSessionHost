namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionScreenSnapshotHistoryDto(
    string SessionId,
    IReadOnlyList<SessionScreenSnapshotSummaryDto> Entries);
