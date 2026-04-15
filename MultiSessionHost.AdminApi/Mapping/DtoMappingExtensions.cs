using System.Text.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

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

    public static DesktopTargetProfileDto ToDto(this DesktopTargetProfile profile) =>
        new(
            profile.ProfileName,
            profile.Kind.ToString(),
            profile.ProcessName,
            profile.WindowTitleFragment,
            profile.CommandLineFragmentTemplate,
            profile.BaseAddressTemplate,
            profile.MatchingMode.ToString(),
            profile.Metadata,
            profile.SupportsUiSnapshots,
            profile.SupportsStateEndpoint);

    public static DesktopTargetProfileOverrideDto ToDto(this DesktopTargetProfileOverride profileOverride) =>
        new(
            profileOverride.ProcessName,
            profileOverride.WindowTitleFragment,
            profileOverride.CommandLineFragmentTemplate,
            profileOverride.BaseAddressTemplate,
            profileOverride.MatchingMode?.ToString(),
            profileOverride.Metadata,
            profileOverride.SupportsUiSnapshots,
            profileOverride.SupportsStateEndpoint);

    public static SessionTargetBindingDto ToDto(this SessionTargetBinding binding) =>
        new(
            binding.SessionId.Value,
            binding.TargetProfileName,
            binding.Variables,
            binding.Overrides?.ToDto());

    public static ResolvedDesktopTargetDto ToDto(this DesktopSessionTarget target) =>
        new(
            target.SessionId.Value,
            target.ProfileName,
            target.Kind.ToString(),
            target.MatchingMode.ToString(),
            target.ProcessName,
            target.WindowTitleFragment,
            target.CommandLineFragment,
            target.BaseAddress?.ToString(),
            target.Metadata);

    public static SessionTargetAttachmentDto ToDto(this DesktopSessionAttachment attachment) =>
        new(
            attachment.Process.ProcessId,
            attachment.Process.ProcessName,
            attachment.Process.CommandLine,
            attachment.Process.MainWindowHandle,
            attachment.Window.WindowHandle,
            attachment.Window.Title,
            attachment.BaseAddress?.ToString(),
            attachment.AttachedAtUtc);

    public static SessionTargetDto ToDto(
        this ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        IDesktopTargetAdapter adapter) =>
        new(
            context.SessionId.Value,
            context.Profile.ToDto(),
            context.Binding.ToDto(),
            context.Target.ToDto(),
            attachment?.ToDto(),
            adapter.Kind.ToString(),
            adapter.GetType().Name);

    private static JsonElement? ParseRawSnapshot(string? rawSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(rawSnapshotJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonElement>(rawSnapshotJson);
    }
}
