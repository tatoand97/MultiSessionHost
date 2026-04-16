using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed record SessionScreenSnapshotHistory(
    SessionId SessionId,
    IReadOnlyList<SessionScreenSnapshotSummary> Entries);
