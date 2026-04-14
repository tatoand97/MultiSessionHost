using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Core.Models;

public sealed record SessionUiState(
    SessionId SessionId,
    DateTimeOffset? LastRefreshRequestedAtUtc,
    DateTimeOffset? LastRefreshCompletedAtUtc,
    DateTimeOffset? LastRefreshErrorAtUtc,
    DateTimeOffset? LastSnapshotCapturedAtUtc,
    string? LastRefreshError,
    string? RawSnapshotJson,
    UiTree? ProjectedTree,
    UiTreeDiff? LastDiff,
    IReadOnlyList<PlannedUiWorkItem> PlannedWorkItems)
{
    public static SessionUiState Create(SessionId sessionId) =>
        new(
            sessionId,
            LastRefreshRequestedAtUtc: null,
            LastRefreshCompletedAtUtc: null,
            LastRefreshErrorAtUtc: null,
            LastSnapshotCapturedAtUtc: null,
            LastRefreshError: null,
            RawSnapshotJson: null,
            ProjectedTree: null,
            LastDiff: null,
            PlannedWorkItems: []);
}
