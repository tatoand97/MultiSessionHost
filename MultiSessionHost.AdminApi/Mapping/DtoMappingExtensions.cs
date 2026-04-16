using System.Text.Json;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;

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

    public static SessionScreenSnapshotDto ToDto(this SessionScreenSnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.Sequence,
            snapshot.CapturedAtUtc,
            snapshot.ProcessId,
            snapshot.ProcessName,
            snapshot.WindowHandle,
            snapshot.WindowTitle,
            snapshot.WindowBounds,
            snapshot.ImageWidth,
            snapshot.ImageHeight,
            snapshot.ImageFormat,
            snapshot.PixelFormat,
            snapshot.PayloadByteLength,
            snapshot.TargetKind.ToString(),
            snapshot.CaptureSource,
            snapshot.ObservabilityBackend,
            snapshot.CaptureBackend,
            snapshot.CaptureDurationMs,
            snapshot.CaptureOrigin,
            snapshot.ImageBytes,
            snapshot.Metadata);

    public static SessionScreenSnapshotSummaryDto ToDto(this SessionScreenSnapshotSummary summary) =>
        new(
            summary.SessionId.Value,
            summary.Sequence,
            summary.CapturedAtUtc,
            summary.ProcessId,
            summary.ProcessName,
            summary.WindowHandle,
            summary.WindowTitle,
            summary.WindowBounds,
            summary.ImageWidth,
            summary.ImageHeight,
            summary.ImageFormat,
            summary.PixelFormat,
            summary.PayloadByteLength,
            summary.TargetKind.ToString(),
            summary.CaptureSource,
            summary.ObservabilityBackend,
            summary.CaptureBackend,
            summary.CaptureDurationMs,
            summary.CaptureOrigin,
            summary.Metadata);

    public static SessionScreenSnapshotHistoryDto ToDto(this SessionScreenSnapshotHistory history) =>
        new(
            history.SessionId.Value,
            history.Entries.Select(static entry => entry.ToDto()).ToArray());

    public static ScreenRegionMatchDto ToDto(this ScreenRegionMatch match) =>
        new(
            match.RegionName,
            match.RegionKind,
            match.Bounds,
            match.Confidence,
            match.SourceLocatorName,
            match.ResolutionReason,
            match.MatchState.ToString(),
            match.AnchorStrategy,
            match.TargetImageWidth,
            match.TargetImageHeight,
            match.Metadata);

    public static SessionScreenRegionResolutionDto ToDto(this SessionScreenRegionResolution resolution) =>
        new(
            resolution.SessionId.Value,
            resolution.ResolvedAtUtc,
            resolution.SourceSnapshotSequence,
            resolution.SourceSnapshotCapturedAtUtc,
            resolution.TargetKind.ToString(),
            resolution.ObservabilityBackend,
            resolution.CaptureBackend,
            resolution.TargetProfileName,
            resolution.RegionLayoutProfile,
            resolution.LocatorSetName,
            resolution.LocatorName,
            resolution.TargetImageWidth,
            resolution.TargetImageHeight,
            resolution.TotalRegionsRequested,
            resolution.MatchedRegionCount,
            resolution.MissingRegionCount,
            resolution.Regions.Select(static region => region.ToDto()).ToArray(),
            resolution.Warnings,
            resolution.Errors,
            resolution.Metadata);

    public static SessionScreenRegionSummaryDto ToDto(this SessionScreenRegionSummary summary) =>
        new(
            summary.SessionId.Value,
            summary.ResolvedAtUtc,
            summary.SourceSnapshotSequence,
            summary.SourceSnapshotCapturedAtUtc,
            summary.TargetKind.ToString(),
            summary.ObservabilityBackend,
            summary.CaptureBackend,
            summary.TargetProfileName,
            summary.RegionLayoutProfile,
            summary.LocatorSetName,
            summary.LocatorName,
            summary.TargetImageWidth,
            summary.TargetImageHeight,
            summary.TotalRegionsRequested,
            summary.MatchedRegionCount,
            summary.MissingRegionCount,
            summary.Warnings,
            summary.Errors,
            summary.Metadata);

    public static ProcessedFrameArtifactDto ToDto(this ProcessedFrameArtifact artifact) =>
        new(
            artifact.ArtifactName,
            artifact.ArtifactKind,
            artifact.SourceSnapshotSequence,
            artifact.SourceRegionName,
            artifact.OutputWidth,
            artifact.OutputHeight,
            artifact.ImageFormat,
            artifact.PayloadByteLength,
            artifact.PreprocessingSteps,
            artifact.Warnings,
            artifact.Errors,
            artifact.Metadata,
            artifact.ImageBytes);

    public static ProcessedFrameArtifactSummaryDto ToDto(this ProcessedFrameArtifactSummary artifact) =>
        new(
            artifact.ArtifactName,
            artifact.ArtifactKind,
            artifact.SourceSnapshotSequence,
            artifact.SourceRegionName,
            artifact.OutputWidth,
            artifact.OutputHeight,
            artifact.ImageFormat,
            artifact.PayloadByteLength,
            artifact.PreprocessingSteps,
            artifact.Warnings,
            artifact.Errors,
            artifact.Metadata);

    public static SessionFramePreprocessingResultDto ToDto(this SessionFramePreprocessingResult result) =>
        new(
            result.SessionId.Value,
            result.ProcessedAtUtc,
            result.SourceSnapshotSequence,
            result.SourceSnapshotCapturedAtUtc,
            result.SourceRegionResolutionSequence,
            result.SourceRegionResolutionResolvedAtUtc,
            result.TargetKind.ToString(),
            result.ObservabilityBackend,
            result.CaptureBackend,
            result.PreprocessingProfileName,
            result.TotalArtifactCount,
            result.SuccessfulArtifactCount,
            result.FailedArtifactCount,
            result.Artifacts.Select(static artifact => artifact.ToDto()).ToArray(),
            result.Warnings,
            result.Errors,
            result.Metadata);

    public static SessionFramePreprocessingSummaryDto ToDto(this SessionFramePreprocessingSummary summary) =>
        new(
            summary.SessionId.Value,
            summary.ProcessedAtUtc,
            summary.SourceSnapshotSequence,
            summary.SourceSnapshotCapturedAtUtc,
            summary.SourceRegionResolutionSequence,
            summary.SourceRegionResolutionResolvedAtUtc,
            summary.TargetKind.ToString(),
            summary.ObservabilityBackend,
            summary.CaptureBackend,
            summary.PreprocessingProfileName,
            summary.TotalArtifactCount,
            summary.SuccessfulArtifactCount,
            summary.FailedArtifactCount,
            summary.Artifacts.Select(static artifact => artifact.ToDto()).ToArray(),
            summary.Warnings,
            summary.Errors,
            summary.Metadata);

    public static SessionRecoverySnapshotDto ToDto(this SessionRecoverySnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.RecoveryStatus.ToString(),
            snapshot.CircuitBreakerState.ToString(),
            snapshot.ConsecutiveFailureCount,
            snapshot.FailureCountsByCategory.ToDictionary(static entry => entry.Key.ToString(), static entry => entry.Value),
            snapshot.LastFailureAtUtc,
            snapshot.LastSuccessAtUtc,
            snapshot.BackoffUntilUtc,
            snapshot.NextRecoveryAttemptAtUtc,
            snapshot.IsSnapshotStale,
            snapshot.IsAttachmentInvalid,
            snapshot.IsTargetQuarantined,
            snapshot.TargetQuarantineReasonCode,
            snapshot.MetadataDriftDetected,
            snapshot.AdapterHealthState.ToString(),
            snapshot.LastRecoveryAction,
            snapshot.LastRecoveryReasonCode,
            snapshot.LastRecoveryReason,
            snapshot.LastTransitionAtUtc,
            snapshot.IsBlockedFromRecoveryAttempts,
            snapshot.HalfOpenProbeAttempts,
            snapshot.Metadata);

    public static SessionRecoveryHistoryEntryDto ToDto(this SessionRecoveryHistoryEntry entry) =>
        new(
            entry.SessionId.Value,
            entry.OccurredAtUtc,
            entry.Action,
            entry.RecoveryStatus.ToString(),
            entry.CircuitBreakerState.ToString(),
            entry.AdapterHealthState.ToString(),
            entry.FailureCategory?.ToString(),
            entry.ReasonCode,
            entry.Reason,
            entry.Metadata);

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
            result.Packages.Select(static item => item.ToDto()).ToArray(),
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
            result.Packages.Count,
            result.Packages.Select(static package => package.PackageName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            result.Warnings,
            result.ConfidenceSummary.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal));

    public static TargetSemanticPackageResultDto ToDto(this TargetSemanticPackageResult result) =>
        new(
            result.PackageName,
            result.PackageVersion,
            result.Succeeded,
            result.Confidence.ToString(),
            result.Warnings,
            result.ConfidenceSummary.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal),
            result.FailureReason,
            result.EveLike is null ? null : result.EveLike.ToDto());

    public static EveLikeSemanticPackageResultDto ToDto(this EveLikeSemanticPackageResult result) =>
        new(
            result.PackageName,
            result.PackageVersion,
            result.Presence.ToDto(),
            result.TravelRoute.ToDto(),
            result.OverviewEntries.Select(static item => item.ToDto()).ToArray(),
            result.ProbeScannerEntries.Select(static item => item.ToDto()).ToArray(),
            result.Tactical.ToDto(),
            result.Safety.ToDto(),
            result.Warnings,
            result.ConfidenceSummary.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToString(), StringComparer.Ordinal));

    public static LocalPresenceSnapshotDto ToDto(this LocalPresenceSnapshot result) =>
        new(
            result.IsVisible,
            result.PanelLabel,
            result.VisibleEntityCount,
            result.TotalEntityCount,
            result.Entities.Select(static item => item.ToDto()).ToArray(),
            result.Confidence.ToString(),
            result.Warnings);

    public static PresenceEntitySemanticDto ToDto(this PresenceEntitySemantic result) =>
        new(
            result.Label,
            result.Standing,
            result.Tags,
            result.SourceNodeIds,
            result.Count,
            result.Confidence.ToString());

    public static TravelRouteSnapshotDto ToDto(this TravelRouteSnapshot result) =>
        new(
            result.RouteActive,
            result.DestinationLabel,
            result.CurrentLocationLabel,
            result.NextWaypointLabel,
            result.WaypointCount,
            result.VisibleWaypoints,
            result.ProgressPercent,
            result.Confidence.ToString(),
            result.Reasons);

    public static OverviewEntrySemanticDto ToDto(this OverviewEntrySemantic result) =>
        new(
            result.Label,
            result.Category,
            result.DistanceText,
            result.DistanceValue,
            result.Selected,
            result.Targeted,
            result.Disposition,
            result.SourceNodeIds,
            result.Confidence.ToString(),
            result.Warnings);

    public static ProbeScannerEntrySemanticDto ToDto(this ProbeScannerEntrySemantic result) =>
        new(
            result.Label,
            result.SignatureType,
            result.Status,
            result.DistanceText,
            result.DistanceValue,
            result.SourceNodeIds,
            result.Confidence.ToString(),
            result.Warnings);

    public static TacticalSnapshotDto ToDto(this TacticalSnapshot result) =>
        new(
            result.PrimaryVisibleObjects.Select(static item => item.ToDto()).ToArray(),
            result.HostileCandidateCount,
            result.SelectedTargetLabels,
            result.NearbyObjectHints,
            result.EngagementAlerts,
            result.Confidence.ToString(),
            result.Warnings);

    public static SafetyLocationSemanticDto ToDto(this SafetyLocationSemantic result) =>
        new(
            result.IsSafeLocation,
            result.SafeLocationLabel,
            result.HideAvailable,
            result.DockedHint,
            result.TetheredHint,
            result.EscapeRouteLabel,
            result.Confidence.ToString(),
            result.Reasons);

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

    public static DecisionPlanHistoryEntryDto ToDto(this DecisionPlanHistoryEntry entry) =>
        new(
            entry.SessionId.Value,
            entry.RecordedAtUtc,
            entry.Plan.ToDto());

    public static DecisionPlanHistoryDto ToDecisionHistoryDto(this SessionId sessionId, IReadOnlyList<DecisionPlanHistoryEntry> entries) =>
        new(
            sessionId.Value,
            entries.Select(static entry => entry.ToDto()).ToArray());

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

    public static PolicyRuleSetDto ToDto(this PolicyRuleSet rules) =>
        new(
            rules.SiteSelectionAllowRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.SiteSelectionFallbackRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.ThreatResponseRetreatRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.ThreatResponseDenyRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.ThreatResponseFallbackRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.TargetPriorityRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.TargetDenyRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.TargetFallbackRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.ResourceUsageRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.ResourceUsageFallbackRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.TransitRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.TransitFallbackRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.AbortRules.Select(static rule => rule.ToDto()).ToArray(),
            rules.AbortFallbackRules.Select(static rule => rule.ToDto()).ToArray());

    public static PolicyRuleDto ToDto(this PolicyRule rule) =>
        new(
            rule.PolicyName,
            rule.RuleName,
            rule.Family.ToString(),
            rule.RuleFamily,
            rule.RuleIntent,
            rule.SourceScope,
            rule.IsFallback,
            rule.MatchLabels,
            rule.LabelMatchMode.ToString(),
            rule.MatchTypes,
            rule.TypeMatchMode.ToString(),
            rule.MatchTags,
            rule.RequireAllTags,
            rule.AllowedThreatSeverities.Select(static value => value.ToString()).ToArray(),
            rule.MinThreatSeverity?.ToString(),
            rule.MinRiskSeverity?.ToString(),
            rule.MatchSuggestedPolicies.Select(static value => value.ToString()).ToArray(),
            rule.MatchSessionStatuses.Select(static value => value.ToString()).ToArray(),
            rule.MatchNavigationStatuses.Select(static value => value.ToString()).ToArray(),
            rule.RequireTransitioning,
            rule.RequireDestination,
            rule.RequireIdleNavigation,
            rule.RequireIdleActivity,
            rule.RequireNoActiveTarget,
            rule.RequireActiveTarget,
            rule.MatchResourceCritical,
            rule.MatchResourceDegraded,
            rule.RequireDefensivePosture,
            rule.MinProgressPercent,
            rule.MaxProgressPercent,
            rule.MinResourcePercent,
            rule.MaxResourcePercent,
            rule.MinWarningCount,
            rule.MaxWarningCount,
            rule.MinUnknownCount,
            rule.MaxUnknownCount,
            rule.MinAvailableCount,
            rule.MaxAvailableCount,
            rule.MinConfidence,
            rule.MaxConfidence,
            rule.MetricName,
            rule.MinMetricValue,
            rule.MaxMetricValue,
            rule.DirectiveKind.ToString(),
            rule.Priority,
            rule.SuggestedPolicy,
            rule.Blocks,
            rule.Aborts,
            rule.MinimumWait.TotalMilliseconds,
            rule.ThresholdName,
            rule.PolicyMode,
            rule.TargetLabelTemplate,
            rule.Reason);

    public static DecisionPlanExplanationDto ToExplanationDto(this DecisionPlan plan, PolicyRuleSet effectiveRules) =>
        new(
            plan.SessionId.Value,
            plan.PlannedAtUtc,
            effectiveRules.ToDto(),
            (plan.Explanation?.PolicyEvaluations ?? []).Select(static explanation => explanation.ToDto()).ToArray(),
            (plan.Explanation?.AggregationRulesApplied ?? []).Select(static trace => trace.ToDto()).ToArray(),
            plan.Directives.Select(static directive => directive.ToDto()).ToArray(),
            plan.Reasons.Select(static reason => reason.ToDto()).ToArray(),
            plan.Warnings,
            plan.Explanation?.FinalReasonCodes ?? []);

    public static PolicyEvaluationExplanationDto ToDto(this PolicyEvaluationExplanation explanation) =>
        new(
            explanation.PolicyName,
            explanation.CandidateSummary,
            explanation.RuleTraces.Select(static trace => trace.ToDto()).ToArray(),
            explanation.MatchedRuleName,
            explanation.FallbackUsed,
            explanation.ProducedDirectiveKinds,
            explanation.MemoryInfluences.Select(static trace => trace.ToDto()).ToArray());

    public static MemoryInfluenceTraceDto ToDto(this MemoryInfluenceTrace trace) =>
        new(
            trace.PolicyName,
            trace.InfluenceType,
            trace.MemoryKey,
            trace.ReasonCode,
            trace.Reason,
            trace.Value,
            trace.Metadata);

    public static PolicyRuleEvaluationTraceDto ToDto(this PolicyRuleEvaluationTrace trace) =>
        new(
            trace.PolicyName,
            trace.RuleFamily,
            trace.RuleName,
            trace.RuleIntent,
            trace.IsFallback,
            trace.CandidateId,
            trace.CandidateLabel,
            trace.Outcome.ToString(),
            trace.MatchedCriteria,
            trace.RejectedReason,
            trace.ProducedDirectiveKinds,
            trace.Blocks,
            trace.Aborts);

    public static AggregationRuleApplicationTraceDto ToDto(this AggregationRuleApplicationTrace trace) =>
        new(
            trace.RuleName,
            trace.RuleType,
            trace.Applied,
            trace.Reason,
            trace.TriggerDirectiveKinds,
            trace.SuppressedDirectiveIds,
            trace.ResultStatus);

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

    public static SessionActivitySnapshotDto ToDto(this SessionActivitySnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.CurrentState.ToString(),
            snapshot.PreviousState?.ToString(),
            snapshot.LastTransitionAtUtc,
            snapshot.LastReasonCode,
            snapshot.LastReason,
            snapshot.LastMetadata,
            snapshot.History.Select(static entry => entry.ToDto()).ToArray(),
            snapshot.IsTerminal);

    public static SessionActivityHistoryEntryDto ToDto(this SessionActivityHistoryEntry entry) =>
        new(
            entry.FromState.ToString(),
            entry.ToState.ToString(),
            entry.ReasonCode,
            entry.Reason,
            entry.OccurredAtUtc,
            entry.Metadata);

    public static SessionActivityHistoryDto ToHistoryDto(this SessionActivitySnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.History.Select(static entry => entry.ToDto()).ToArray());

    public static DecisionPlanExecutionDto ToDto(this DecisionPlanExecutionResult result) =>
        new(
            result.SessionId.Value,
            result.PlanFingerprint,
            result.ExecutedAtUtc,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.ExecutionStatus.ToString(),
            result.WasAutoExecuted,
            result.DirectiveResults.Select(static directive => directive.ToDto()).ToArray(),
            result.Summary.ToDto(),
            result.DeferredUntilUtc,
            result.FailureReason,
            result.Warnings,
            result.Metadata);

    public static DecisionDirectiveExecutionResultDto ToDto(this DecisionDirectiveExecutionResult result) =>
        new(
            result.DirectiveId,
            result.DirectiveKind.ToString(),
            result.PolicyName,
            result.Priority,
            result.Status.ToString(),
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.Message,
            result.FailureCode,
            result.DeferredUntilUtc,
            result.Metadata);

    public static DecisionPlanExecutionSummaryDto ToDto(this DecisionPlanExecutionSummary summary) =>
        new(
            summary.TotalDirectives,
            summary.SucceededCount,
            summary.FailedCount,
            summary.SkippedCount,
            summary.DeferredCount,
            summary.NotHandledCount,
            summary.BlockedCount,
            summary.AbortedCount,
            summary.ExecutedDirectiveKinds,
            summary.SkippedDirectiveKinds,
            summary.UnhandledDirectiveKinds);

    public static DecisionPlanExecutionHistoryEntryDto ToDto(this DecisionPlanExecutionRecord record) =>
        new(
            record.SessionId.Value,
            record.RecordedAtUtc,
            record.Result.ToDto());

    public static DecisionPlanExecutionHistoryDto ToHistoryDto(this SessionId sessionId, IReadOnlyList<DecisionPlanExecutionRecord> records) =>
        new(
            sessionId.Value,
            records.Select(static record => record.ToDto()).ToArray());

    public static SessionOperationalMemorySnapshotDto ToDto(this SessionOperationalMemorySnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.CapturedAtUtc,
            snapshot.UpdatedAtUtc,
            snapshot.Summary.ToDto(),
            snapshot.KnownWorksites.Select(static item => item.ToDto()).ToArray(),
            snapshot.RecentRiskObservations.Select(static item => item.ToDto()).ToArray(),
            snapshot.RecentPresenceObservations.Select(static item => item.ToDto()).ToArray(),
            snapshot.RecentTimingObservations.Select(static item => item.ToDto()).ToArray(),
            snapshot.RecentOutcomeObservations.Select(static item => item.ToDto()).ToArray(),
            snapshot.Warnings,
            snapshot.Metadata);

    public static SessionOperationalMemorySummaryDto ToDto(this SessionOperationalMemorySummary summary) =>
        new(
            summary.KnownWorksiteCount,
            summary.ActiveRiskMemoryCount,
            summary.ActivePresenceMemoryCount,
            summary.TimingObservationCount,
            summary.OutcomeObservationCount,
            summary.LastUpdatedAtUtc,
            summary.TopRememberedRiskSeverity.ToString(),
            summary.MostRecentOutcomeKind);

    public static WorksiteObservationDto ToDto(this WorksiteObservation observation) =>
        new(
            observation.WorksiteKey,
            observation.WorksiteLabel,
            observation.Tags,
            observation.FirstObservedAtUtc,
            observation.LastObservedAtUtc,
            observation.LastSelectedAtUtc,
            observation.LastArrivedAtUtc,
            observation.LastOutcome,
            observation.LastObservedRiskSeverity.ToString(),
            observation.OccupancySignals,
            observation.VisitCount,
            observation.SuccessCount,
            observation.FailureCount,
            observation.LastKnownConfidence,
            observation.IsStale,
            observation.Metadata);

    public static RiskObservationDto ToDto(this RiskObservation observation) =>
        new(
            observation.ObservationId,
            observation.EntityKey,
            observation.EntityLabel,
            observation.SourceKey,
            observation.SourceLabel,
            observation.Severity.ToString(),
            observation.SuggestedPolicy.ToString(),
            observation.RuleName,
            observation.FirstObservedAtUtc,
            observation.LastObservedAtUtc,
            observation.Count,
            observation.LastKnownConfidence,
            observation.IsStale,
            observation.Metadata);

    public static PresenceObservationDto ToDto(this PresenceObservation observation) =>
        new(
            observation.ObservationId,
            observation.EntityKey,
            observation.EntityLabel,
            observation.EntityType,
            observation.Status,
            observation.FirstObservedAtUtc,
            observation.LastObservedAtUtc,
            observation.Count,
            observation.LastKnownConfidence,
            observation.IsStale,
            observation.Metadata);

    public static TimingObservationDto ToDto(this TimingObservation observation) =>
        new(
            observation.TimingKey,
            observation.Kind,
            observation.FirstObservedAtUtc,
            observation.LastObservedAtUtc,
            observation.LastDurationMs,
            observation.SampleCount,
            observation.MinDurationMs,
            observation.MaxDurationMs,
            observation.AverageDurationMs,
            observation.IsStale,
            observation.Metadata);

    public static OutcomeObservationDto ToDto(this OutcomeObservation observation) =>
        new(
            observation.OutcomeId,
            observation.RelatedWorksiteKey,
            observation.RelatedDirectiveKind,
            observation.RelatedActivityState,
            observation.ResultKind,
            observation.ObservedAtUtc,
            observation.Message,
            observation.IsStale,
            observation.Metadata);

    public static MemoryObservationRecordDto ToDto(this MemoryObservationRecord record) =>
        new(
            record.ObservationId,
            record.SessionId.Value,
            record.Category.ToString(),
            record.ObservationKey,
            record.ObservedAtUtc,
            record.Source,
            record.Summary,
            record.Metadata);

    public static SessionOperationalMemoryHistoryDto ToMemoryHistoryDto(this SessionId sessionId, IReadOnlyList<MemoryObservationRecord> records) =>
        new(
            sessionId.Value,
            records.Select(static record => record.ToDto()).ToArray());

    public static PolicyMemoryContextDto ToDto(this PolicyMemoryContext context) =>
        new(
            context.SessionId.Value,
            context.CapturedAtUtc,
            context.KnownWorksites.Select(static item => item.ToDto()).ToArray(),
            context.RiskSummary.ToDto(),
            context.PresenceSummary.ToDto(),
            context.TimingSummary.ToDto(),
            context.OutcomeSummary.ToDto(),
            context.Warnings,
            context.Metadata);

    public static WorksiteMemorySummaryDto ToDto(this WorksiteMemorySummary summary) =>
        new(
            summary.WorksiteKey,
            summary.WorksiteLabel,
            summary.VisitCount,
            summary.SuccessCount,
            summary.FailureCount,
            summary.SuccessRate,
            summary.LastOutcome,
            summary.LastObservedRiskSeverity.ToString(),
            summary.LastSelectedAtUtc,
            summary.LastArrivedAtUtc,
            summary.OccupancySignalCount,
            summary.IsStale,
            summary.Confidence,
            summary.Tags,
            summary.Metadata);

    public static RiskMemorySummaryDto ToDto(this RiskMemorySummary summary) =>
        new(
            summary.HighestRecentSeverity.ToString(),
            summary.RepeatedHighRiskCount,
            summary.RepeatedUnknownRiskCount,
            summary.TopSources,
            summary.TopSuggestedPolicies,
            summary.HasRepeatedWithdrawLikePattern,
            summary.Metadata);

    public static PresenceMemorySummaryDto ToDto(this PresenceMemorySummary summary) =>
        new(
            summary.TotalPresenceSignals,
            summary.RecentPresenceSignals,
            summary.LastPresenceSignalAtUtc,
            summary.IsRecentlyOccupied,
            summary.Metadata);

    public static TimingMemorySummaryDto ToDto(this TimingMemorySummary summary) =>
        new(
            summary.KnownTimingKinds,
            summary.AverageTransitionDurationMs,
            summary.AverageArrivalDelayMs,
            summary.AverageWaitWindowMs,
            summary.HasRepeatedLongWaitPattern,
            summary.Metadata);

    public static OutcomeMemorySummaryDto ToDto(this OutcomeMemorySummary summary) =>
        new(
            summary.MostRecentOutcomeKind,
            summary.SuccessCount,
            summary.FailureCount,
            summary.DeferredCount,
            summary.AbortCount,
            summary.NoOpCount,
            summary.HasRecentFailurePattern,
            summary.Metadata);

    public static RuntimePersistenceStatusDto ToDto(this RuntimePersistenceStatusSnapshot snapshot) =>
        new(
            snapshot.Enabled,
            snapshot.Mode,
            snapshot.BasePath,
            snapshot.SchemaVersion,
            snapshot.CapturedAtUtc,
            snapshot.Sessions.Select(static session => session.ToDto()).ToArray());

    public static RuntimePersistenceSessionStatusDto ToDto(this RuntimePersistenceSessionStatus status) =>
        new(
            status.SessionId.Value,
            status.Rehydrated,
            status.LastLoadedAtUtc,
            status.LastSavedAtUtc,
            status.LastError,
            status.PersistedPath,
            status.ActivityHistoryCount,
            status.OperationalMemoryHistoryCount,
            status.DecisionPlanHistoryCount,
            status.DecisionExecutionHistoryCount,
            status.PolicyControlHistoryCount,
            status.RecoveryHistoryCount);

    public static SessionPolicyControlStateDto ToDto(this SessionPolicyControlState state) =>
        new(
            state.SessionId.Value,
            state.IsPolicyPaused,
            state.PausedAtUtc,
            state.ResumedAtUtc,
            state.LastChangedAtUtc,
            state.ReasonCode,
            state.Reason,
            state.ChangedBy,
            state.Metadata);

    public static SessionPolicyControlHistoryEntryDto ToDto(this SessionPolicyControlHistoryEntry entry) =>
        new(
            entry.SessionId.Value,
            entry.Action.ToString(),
            entry.OccurredAtUtc,
            entry.ReasonCode,
            entry.Reason,
            entry.ChangedBy,
            entry.Metadata);

    public static PolicyControlActionResultDto ToDto(this PolicyControlActionResult result) =>
        new(
            result.State.SessionId.Value,
            result.Action.ToString(),
            result.WasChanged,
            result.State.ToDto(),
            result.History.Select(static entry => entry.ToDto()).ToArray(),
            result.Message);

    public static SessionObservabilityEventDto ToDto(this SessionObservabilityEvent sessionEvent) =>
        new(
            sessionEvent.SessionId.Value,
            sessionEvent.EventId.ToString(),
            sessionEvent.EventType,
            sessionEvent.Category,
            sessionEvent.OccurredAtUtc,
            sessionEvent.DurationMs,
            sessionEvent.Outcome,
            sessionEvent.Severity.ToString(),
            sessionEvent.ReasonCode,
            sessionEvent.Reason,
            sessionEvent.SourceComponent,
            sessionEvent.CorrelationId,
            sessionEvent.TraceId,
            sessionEvent.Metadata);

    public static SessionLatencyMeasurementDto ToDto(this SessionLatencyMeasurement measurement) =>
        new(
            measurement.SessionId.Value,
            measurement.EventId.ToString(),
            measurement.Stage,
            measurement.Category,
            measurement.OccurredAtUtc,
            measurement.DurationMs ?? 0,
            measurement.Outcome,
            measurement.ReasonCode,
            measurement.Reason,
            measurement.SourceComponent,
            measurement.CorrelationId,
            measurement.TraceId,
            measurement.Metadata);

    public static SessionReasonMetricDto ToDto(this SessionReasonMetric metric) =>
        new(
            metric.SessionId.Value,
            metric.Category,
            metric.ReasonCode,
            metric.Reason,
            metric.Count,
            metric.LastOccurredAtUtc,
            metric.SourceComponent);

    public static AdapterErrorRecordDto ToDto(this AdapterErrorRecord record) =>
        new(
            record.SessionId.Value,
            record.EventId.ToString(),
            record.OccurredAtUtc,
            record.AdapterName,
            record.Operation,
            record.ExceptionType,
            record.Message,
            record.ReasonCode,
            record.SourceComponent,
            record.CorrelationId,
            record.TraceId,
            record.Metadata);

    public static AttachmentLifecycleEventDto ToDto(this AttachmentLifecycleEvent record) =>
        new(
            record.SessionId.Value,
            record.EventId.ToString(),
            record.OccurredAtUtc,
            record.Operation,
            record.AdapterName,
            record.TargetKind,
            record.Outcome,
            record.DurationMs,
            record.ReasonCode,
            record.Reason,
            record.SourceComponent,
            record.CorrelationId,
            record.TraceId,
            record.Metadata);

    public static PersistenceLifecycleEventDto ToDto(this PersistenceLifecycleEvent record) =>
        new(
            record.SessionId.Value,
            record.EventId.ToString(),
            record.OccurredAtUtc,
            record.Operation,
            record.Outcome,
            record.DurationMs,
            record.Path,
            record.ItemCount,
            record.ReasonCode,
            record.Reason,
            record.SourceComponent,
            record.CorrelationId,
            record.TraceId,
            record.Metadata);

    public static SessionObservabilitySummaryDto ToDto(this SessionObservabilitySummary summary) =>
        new(
            summary.SessionId.Value,
            summary.LastUpdatedAtUtc,
            summary.Status.ToString(),
            summary.SnapshotCount,
            summary.ExtractionCount,
            summary.DomainProjectionCount,
            summary.PolicyEvaluationCount,
            summary.DecisionExecutionCount,
            summary.CommandExecutionCount,
            summary.PersistenceFlushCount,
            summary.PersistenceRehydrateCount,
            summary.AttachCount,
            summary.ReattachCount,
            summary.AdapterErrorCount,
            summary.WithdrawCount,
            summary.AbortCount,
            summary.HideCount,
            summary.WaitCount,
            summary.SkippedExecutionCount,
            summary.CommandFailureCount,
            summary.PersistenceFailureCount,
            summary.RecentWarningCount,
            summary.RecentErrorCount,
            summary.LastSnapshotDurationMs,
            summary.LastExtractionDurationMs,
            summary.LastPolicyEvaluationDurationMs,
            summary.LastDecisionExecutionDurationMs,
            summary.LastCommandDurationMs,
            summary.LastPersistenceFlushDurationMs,
            summary.LastPersistenceRehydrateDurationMs,
            summary.LastAttachDurationMs,
            summary.LastReattachDurationMs,
            summary.LastSnapshotOutcome,
            summary.LastExtractionOutcome,
            summary.LastPolicyOutcome,
            summary.LastDecisionOutcome,
            summary.LastCommandOutcome,
            summary.LastPersistenceOutcome,
            summary.LastRehydrateOutcome,
            summary.LastAttachOutcome,
            summary.LastReattachOutcome,
            summary.LastAdapterError,
            summary.LastPersistenceError,
            summary.LastReasonCode,
            summary.LastReason,
            summary.ReasonCounts);

    public static SessionObservabilityMetricsDto ToDto(this SessionObservabilityMetricsSnapshot snapshot) =>
        new(
            snapshot.SessionId.Value,
            snapshot.CapturedAtUtc,
            snapshot.Summary.ToDto(),
            snapshot.RecentLatencies.Select(static latency => latency.ToDto()).ToArray(),
            snapshot.ReasonMetrics.Select(static metric => metric.ToDto()).ToArray(),
            snapshot.RecentErrors.Select(static error => error.ToDto()).ToArray());

    public static SessionObservabilityDto ToDto(this SessionObservabilitySnapshot snapshot) =>
        new(
            snapshot.Summary.ToDto(),
            snapshot.RecentEvents.Select(static sessionEvent => sessionEvent.ToDto()).ToArray(),
            snapshot.Metrics.ToDto(),
            snapshot.RecentErrors.Select(static error => error.ToDto()).ToArray());

    public static GlobalObservabilitySnapshotDto ToDto(this GlobalObservabilitySnapshot snapshot) =>
        new(
            snapshot.CapturedAtUtc,
            snapshot.Status.ToString(),
            snapshot.ActiveSessions,
            snapshot.FaultedSessions,
            snapshot.PausedSessions,
            snapshot.TotalEvents,
            snapshot.TotalErrors,
            snapshot.Sessions.Select(static session => session.ToDto()).ToArray(),
            snapshot.RecentErrors.Select(static error => error.ToDto()).ToArray());
}
