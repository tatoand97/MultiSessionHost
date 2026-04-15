using System.Text.Json;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

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

    public static SessionDomainStateDto ToDto(this SessionDomainState state) =>
        new(
            state.SessionId.Value,
            state.CapturedAtUtc,
            state.UpdatedAtUtc,
            state.Version,
            state.Source.ToString(),
            state.Navigation.ToDto(),
            state.Combat.ToDto(),
            state.Threat.ToDto(),
            state.Target.ToDto(),
            state.Companions.ToDto(),
            state.Resources.ToDto(),
            state.Location.ToDto(),
            state.Warnings);

    public static NavigationStateDto ToDto(this NavigationState state) =>
        new(
            state.Status.ToString(),
            state.IsTransitioning,
            state.DestinationLabel,
            state.RouteLabel,
            state.ProgressPercent,
            state.StartedAtUtc,
            state.UpdatedAtUtc);

    public static CombatStateDto ToDto(this CombatState state) =>
        new(
            state.Status.ToString(),
            state.ActivityPhase,
            state.OffensiveActionsActive,
            state.DefensivePostureActive,
            state.EngagedAtUtc,
            state.UpdatedAtUtc);

    public static ThreatStateDto ToDto(this ThreatState state) =>
        new(
            state.Severity.ToString(),
            state.UnknownCount,
            state.NeutralCount,
            state.HostileCount,
            state.IsSafe,
            state.LastThreatChangedAtUtc,
            state.Signals,
            state.TopSuggestedPolicy,
            state.TopEntityLabel,
            state.TopEntityType);

    public static TargetStateDto ToDto(this TargetState state) =>
        new(
            state.HasActiveTarget,
            state.PrimaryTargetId,
            state.PrimaryTargetLabel,
            state.TrackedTargetCount,
            state.LockedTargetCount,
            state.SelectedTargetCount,
            state.Status.ToString(),
            state.LastTargetChangedAtUtc,
            state.UpdatedAtUtc);

    public static CompanionStateDto ToDto(this CompanionState state) =>
        new(
            state.Status.ToString(),
            state.AreAvailable,
            state.AreHealthy,
            state.ActiveCount,
            state.DeployedCount,
            state.DockedCount,
            state.IdleCount,
            state.UpdatedAtUtc);

    public static ResourceStateDto ToDto(this ResourceState state) =>
        new(
            state.HealthPercent,
            state.CapacityPercent,
            state.EnergyPercent,
            state.AvailableChargeCount,
            state.CapacityCount,
            state.IsDegraded,
            state.IsCritical,
            state.UpdatedAtUtc);

    public static LocationStateDto ToDto(this LocationState state) =>
        new(
            state.ContextLabel,
            state.SubLocationLabel,
            state.IsBaseOrHome,
            state.IsUnknown,
            state.Confidence.ToString(),
            state.ArrivedAtUtc,
            state.UpdatedAtUtc);

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

    public static UiSemanticExtractionResultDto ToDto(this UiSemanticExtractionResult result) =>
        new(
            result.SessionId.Value,
            result.ExtractedAtUtc,
            result.Lists.Select(static item => item.ToDto()).ToArray(),
            result.Targets.Select(static item => item.ToDto()).ToArray(),
            result.Alerts.Select(static item => item.ToDto()).ToArray(),
            result.TransitStates.Select(static item => item.ToDto()).ToArray(),
            result.Resources.Select(static item => item.ToDto()).ToArray(),
            result.Capabilities.Select(static item => item.ToDto()).ToArray(),
            result.PresenceEntities.Select(static item => item.ToDto()).ToArray(),
            result.Warnings,
            result.ConfidenceSummary.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal));

    public static SemanticSummaryDto ToSummaryDto(this UiSemanticExtractionResult result) =>
        new(
            result.SessionId.Value,
            result.ExtractedAtUtc,
            result.Lists.Count,
            result.Targets.Count,
            result.Alerts.Count,
            result.TransitStates.Count,
            result.Resources.Count,
            result.Capabilities.Count,
            result.PresenceEntities.Count,
            result.Warnings,
            result.ConfidenceSummary.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal));

    public static RiskAssessmentResultDto ToDto(this RiskAssessmentResult result) =>
        new(
            result.SessionId.Value,
            result.AssessedAtUtc,
            result.Entities.Select(static entity => entity.ToDto()).ToArray(),
            result.Summary.ToDto(),
            result.Warnings);

    public static RiskAssessmentSummaryDto ToDto(this RiskAssessmentSummary summary) =>
        new(
            summary.SafeCount,
            summary.UnknownCount,
            summary.ThreatCount,
            summary.HighestSeverity.ToString(),
            summary.HighestPriority,
            summary.HasWithdrawPolicy,
            summary.TopCandidateId,
            summary.TopCandidateName,
            summary.TopCandidateType,
            summary.TopSuggestedPolicy.ToString());

    public static RiskEntityAssessmentDto ToDto(this RiskEntityAssessment entity) =>
        new(
            entity.CandidateId,
            entity.Source.ToString(),
            entity.Name,
            entity.Type,
            entity.Tags,
            entity.Disposition.ToString(),
            entity.Severity.ToString(),
            entity.Priority,
            entity.SuggestedPolicy.ToString(),
            entity.MatchedRuleName,
            entity.Reasons,
            entity.Confidence,
            entity.Metadata);

    public static DecisionPlanDto ToDto(this DecisionPlan plan) =>
        new(
            plan.SessionId.Value,
            plan.PlannedAtUtc,
            plan.PlanStatus.ToString(),
            plan.Directives.Select(static directive => directive.ToDto()).ToArray(),
            plan.Reasons.Select(static reason => reason.ToDto()).ToArray(),
            plan.Summary.ToDto(),
            plan.Warnings);

    public static DecisionPlanSummaryDto ToSummaryDto(this DecisionPlan plan) =>
        new(
            plan.SessionId.Value,
            plan.PlannedAtUtc,
            plan.PlanStatus.ToString(),
            plan.Directives.Count,
            plan.Summary.EvaluatedPolicies,
            plan.Summary.MatchedPolicies,
            plan.Summary.BlockingPolicies,
            plan.Summary.AbortingPolicies,
            plan.Warnings);

    public static DecisionDirectiveDto ToDto(this DecisionDirective directive) =>
        new(
            directive.DirectiveId,
            directive.DirectiveKind.ToString(),
            directive.Priority,
            directive.SourcePolicy,
            directive.TargetId,
            directive.TargetLabel,
            directive.SuggestedPolicy,
            directive.Metadata,
            directive.Reasons.Select(static reason => reason.ToDto()).ToArray());

    public static DecisionReasonDto ToDto(this DecisionReason reason) =>
        new(reason.SourcePolicy, reason.Code, reason.Message, reason.Metadata);

    public static PolicyExecutionSummaryDto ToDto(this PolicyExecutionSummary summary) =>
        new(
            summary.EvaluatedPolicies,
            summary.MatchedPolicies,
            summary.BlockingPolicies,
            summary.AbortingPolicies,
            summary.ProducedDirectiveCount,
            summary.ReturnedDirectiveCount,
            summary.SuppressedDirectiveCounts);

    public static DetectedListDto ToDto(this DetectedList item) =>
        new(
            item.NodeId,
            item.Label,
            item.ItemCount,
            item.SelectedItemCount,
            item.VisibleItemLabels,
            item.IsScrollable,
            item.Kind.ToString(),
            item.Confidence.ToString());

    public static DetectedTargetDto ToDto(this DetectedTarget item) =>
        new(
            item.NodeId,
            item.Label,
            item.Selected,
            item.Active,
            item.Focused,
            item.Count,
            item.Index,
            item.Kind.ToString(),
            item.Confidence.ToString());

    public static DetectedAlertDto ToDto(this DetectedAlert item) =>
        new(
            item.NodeId,
            item.Message,
            item.Severity.ToString(),
            item.Visible,
            item.SourceHint,
            item.Confidence.ToString());

    public static DetectedTransitStateDto ToDto(this DetectedTransitState item) =>
        new(
            item.Status.ToString(),
            item.NodeIds,
            item.Label,
            item.ProgressPercent,
            item.Reasons,
            item.Confidence.ToString());

    public static DetectedResourceDto ToDto(this DetectedResource item) =>
        new(
            item.NodeId,
            item.Name,
            item.Kind.ToString(),
            item.Percent,
            item.Value,
            item.Degraded,
            item.Critical,
            item.Confidence.ToString());

    public static DetectedCapabilityDto ToDto(this DetectedCapability item) =>
        new(
            item.NodeId,
            item.Name,
            item.Status.ToString(),
            item.Enabled,
            item.Active,
            item.CoolingDown,
            item.Confidence.ToString());

    public static DetectedPresenceEntityDto ToDto(this DetectedPresenceEntity item) =>
        new(
            item.NodeId,
            item.Label,
            item.Count,
            item.Membership,
            item.Kind.ToString(),
            item.Status,
            item.Confidence.ToString());

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
