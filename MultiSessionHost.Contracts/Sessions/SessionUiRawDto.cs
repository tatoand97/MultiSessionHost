using System.Text.Json;

namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionUiRawDto(
    string SessionId,
    DateTimeOffset? LastRefreshRequestedAtUtc,
    DateTimeOffset? LastRefreshCompletedAtUtc,
    DateTimeOffset? LastRefreshErrorAtUtc,
    DateTimeOffset? LastSnapshotCapturedAtUtc,
    string? LastRefreshError,
    JsonElement? RawSnapshot);
