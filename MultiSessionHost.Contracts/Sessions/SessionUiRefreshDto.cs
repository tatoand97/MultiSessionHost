using System.Text.Json;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionUiRefreshDto(
    string SessionId,
    DateTimeOffset? LastRefreshRequestedAtUtc,
    DateTimeOffset? LastRefreshCompletedAtUtc,
    DateTimeOffset? LastRefreshErrorAtUtc,
    DateTimeOffset? LastSnapshotCapturedAtUtc,
    string? LastRefreshError,
    JsonElement? RawSnapshot,
    UiTree? Tree,
    UiTreeDiff? Diff,
    IReadOnlyList<PlannedUiWorkItem> PlannedWorkItems);
