using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionUiDto(
    string SessionId,
    DateTimeOffset? LastRefreshRequestedAtUtc,
    DateTimeOffset? LastRefreshCompletedAtUtc,
    DateTimeOffset? LastRefreshErrorAtUtc,
    DateTimeOffset? LastSnapshotCapturedAtUtc,
    string? LastRefreshError,
    UiTree? Tree,
    UiTreeDiff? Diff,
    IReadOnlyList<PlannedUiWorkItem> PlannedWorkItems);
