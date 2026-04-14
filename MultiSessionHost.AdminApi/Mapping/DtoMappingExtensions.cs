using System.Text.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.AdminApi.Mapping;

public static class DtoMappingExtensions
{
    public static ProcessHealthDto ToDto(this ProcessHealthSnapshot snapshot) =>
        new(
            snapshot.GeneratedAtUtc,
            snapshot.ActiveSessions,
            snapshot.FaultedSessions,
            snapshot.TotalTicksExecuted,
            snapshot.TotalErrors,
            snapshot.TotalRetries,
            snapshot.TotalHeartbeatsEmitted,
            snapshot.Sessions.Select(static session => session.ToDto()).ToArray());

    public static SessionHealthDto ToDto(this SessionHealthSnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.DisplayName,
            snapshot.CurrentStatus.ToString(),
            snapshot.DesiredStatus.ToString(),
            snapshot.ObservedStatus.ToString(),
            snapshot.LastHeartbeatUtc,
            snapshot.LastError,
            snapshot.IsCircuitOpen,
            snapshot.Metrics.ToDto());

    public static SessionMetricsDto ToDto(this SessionMetricsSnapshot snapshot) =>
        new(
            snapshot.TicksExecuted,
            snapshot.Errors,
            snapshot.Retries,
            snapshot.HeartbeatsEmitted);

    public static SessionInfoDto ToDto(this SessionSnapshot snapshot, SessionHealthSnapshot healthSnapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.Definition.DisplayName,
            snapshot.Definition.Enabled,
            snapshot.Definition.Tags.ToArray(),
            new SessionStateDto(
                snapshot.Runtime.CurrentStatus.ToString(),
                snapshot.Runtime.DesiredStatus.ToString(),
                snapshot.Runtime.ObservedStatus.ToString(),
                snapshot.PendingWorkItems,
                snapshot.Runtime.InFlightWorkItems,
                snapshot.Runtime.StartedAtUtc,
                snapshot.Runtime.LastHeartbeatUtc,
                snapshot.Runtime.LastWorkItemCompletedAtUtc,
                snapshot.Runtime.LastErrorAtUtc,
                snapshot.Runtime.LastError),
            healthSnapshot.Metrics.ToDto());

    public static SessionUiDto ToUiDto(this SessionUiState state) =>
        new(
            state.SessionId.Value,
            state.LastRefreshRequestedAtUtc,
            state.LastRefreshCompletedAtUtc,
            state.LastRefreshErrorAtUtc,
            state.LastSnapshotCapturedAtUtc,
            state.LastRefreshError,
            state.ProjectedTree,
            state.LastDiff,
            state.PlannedWorkItems);

    public static SessionUiRawDto ToUiRawDto(this SessionUiState state) =>
        new(
            state.SessionId.Value,
            state.LastRefreshRequestedAtUtc,
            state.LastRefreshCompletedAtUtc,
            state.LastRefreshErrorAtUtc,
            state.LastSnapshotCapturedAtUtc,
            state.LastRefreshError,
            ParseRawSnapshot(state.RawSnapshotJson));

    public static SessionUiRefreshDto ToUiRefreshDto(this SessionUiState state) =>
        new(
            state.SessionId.Value,
            state.LastRefreshRequestedAtUtc,
            state.LastRefreshCompletedAtUtc,
            state.LastRefreshErrorAtUtc,
            state.LastSnapshotCapturedAtUtc,
            state.LastRefreshError,
            ParseRawSnapshot(state.RawSnapshotJson),
            state.ProjectedTree,
            state.LastDiff,
            state.PlannedWorkItems);

    private static JsonElement? ParseRawSnapshot(string? rawSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(rawSnapshotJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonElement>(rawSnapshotJson);
    }
}
