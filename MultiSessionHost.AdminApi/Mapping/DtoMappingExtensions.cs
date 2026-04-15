using System.Text.Json;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Bindings;
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

    public static BindingStoreSnapshotDto ToDto(this BindingStoreSnapshot snapshot) =>
        new(
            snapshot.Version,
            snapshot.LastUpdatedAtUtc,
            snapshot.Bindings.Select(static binding => binding.ToDto()).ToArray());

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

    public static UiCommandResultDto ToDto(this UiCommandResult result) =>
        new(
            result.Succeeded,
            result.SessionId.Value,
            result.NodeId?.Value,
            result.Kind.ToString(),
            result.Message,
            result.ExecutedAtUtc,
            result.UpdatedUiStateAvailable,
            result.FailureCode);

    public static ExecutionCoordinationSnapshotDto ToDto(this ExecutionCoordinationSnapshot snapshot) =>
        snapshot.ToDto(sessionId: null);

    public static ExecutionCoordinationSnapshotDto ToDto(this ExecutionCoordinationSnapshot snapshot, SessionId sessionId) =>
        snapshot.ToDto((SessionId?)sessionId);

    private static ExecutionCoordinationSnapshotDto ToDto(this ExecutionCoordinationSnapshot snapshot, SessionId? sessionId)
    {
        var activeExecutions = snapshot.ActiveExecutions
            .Where(entry => sessionId is null || entry.Lease.SessionId == sessionId)
            .ToArray();
        var waitingExecutions = snapshot.WaitingExecutions
            .Where(entry => sessionId is null || entry.Request.SessionId == sessionId)
            .ToArray();
        var resourceKeys = activeExecutions
            .SelectMany(static entry => entry.Lease.ResourceSet.GetAllKeys())
            .Concat(waitingExecutions.SelectMany(static entry => entry.Request.ResourceSet.GetAllKeys()))
            .Concat(waitingExecutions.SelectMany(static entry => entry.BlockingResourceKeys))
            .ToHashSet();
        var resources = sessionId is null
            ? snapshot.Resources
            : snapshot.Resources.Where(resource => resourceKeys.Contains(resource.ResourceKey)).ToArray();

        return new ExecutionCoordinationSnapshotDto(
            snapshot.CapturedAtUtc,
            activeExecutions.Select(static entry => entry.ToDto()).ToArray(),
            waitingExecutions.Select(static entry => entry.ToDto()).ToArray(),
            resources.Select(static resource => resource.ToDto()).ToArray(),
            snapshot.TotalAcquisitions,
            snapshot.AverageWaitDurationMs,
            snapshot.CooldownHitCount,
            snapshot.ContentionByScope.Select(static stat => new ExecutionContentionStatDto(stat.Scope.ToString(), stat.ContentionHits)).ToArray());
    }

    private static ActiveExecutionEntryDto ToDto(this ActiveExecutionEntry entry) =>
        new(
            entry.Lease.ExecutionId,
            entry.Lease.SessionId.Value,
            entry.Lease.OperationKind.ToString(),
            entry.Lease.WorkItemKind?.ToString(),
            entry.Lease.UiCommandKind?.ToString(),
            entry.Lease.RequestedAtUtc,
            entry.Lease.AcquiredAtUtc,
            entry.Lease.WaitDuration.TotalMilliseconds,
            entry.RunningDuration.TotalMilliseconds,
            entry.Lease.ResourceSet.ToDto(),
            entry.Lease.Description);

    private static WaitingExecutionEntryDto ToDto(this WaitingExecutionEntry entry) =>
        new(
            entry.Request.ExecutionId,
            entry.Request.SessionId.Value,
            entry.Request.OperationKind.ToString(),
            entry.Request.WorkItemKind?.ToString(),
            entry.Request.UiCommandKind?.ToString(),
            entry.Request.RequestedAtUtc,
            entry.WaitDuration.TotalMilliseconds,
            entry.Request.ResourceSet.ToDto(),
            entry.BlockingResourceKeys.Select(static key => key.ToDto()).ToArray(),
            entry.Request.Description);

    private static ExecutionResourceStateDto ToDto(this ExecutionResourceState resource) =>
        new(
            resource.ResourceKey.ToDto(),
            resource.Capacity,
            resource.ActiveExecutionIds,
            resource.WaitingExecutionIds,
            resource.LastCompletedAtUtc,
            resource.CooldownUntilUtc);

    private static ExecutionResourceSetDto ToDto(this ExecutionResourceSet resourceSet) =>
        new(
            resourceSet.SessionResourceKey.ToDto(),
            resourceSet.TargetResourceKey?.ToDto(),
            resourceSet.GlobalResourceKey?.ToDto(),
            resourceSet.TargetCooldown.TotalMilliseconds);

    private static ExecutionResourceKeyDto ToDto(this ExecutionResourceKey resourceKey) =>
        new(resourceKey.Scope.ToString(), resourceKey.Value);

    private static JsonElement? ParseRawSnapshot(string? rawSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(rawSnapshotJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonElement>(rawSnapshotJson);
    }
}
