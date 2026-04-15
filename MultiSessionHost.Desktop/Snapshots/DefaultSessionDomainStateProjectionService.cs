using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class DefaultSessionDomainStateProjectionService : ISessionDomainStateProjectionService
{
    public SessionDomainState Project(
        SessionDomainState current,
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionUiState? uiState,
        DesktopSessionAttachment? attachment,
        DateTimeOffset now)
    {
        var warnings = BuildWarnings(snapshot, uiState, attachment);
        var hasPlannedWork = uiState?.PlannedWorkItems.Count > 0;
        var isTransitioning =
            snapshot.Runtime.CurrentStatus is SessionStatus.Starting or SessionStatus.Stopping ||
            snapshot.PendingWorkItems > 0 ||
            snapshot.Runtime.InFlightWorkItems > 0 ||
            hasPlannedWork;
        var contextLabel = DesktopTargetMetadata.GetValue(
            context.Target.Metadata,
            DesktopTargetMetadata.UiSource,
            context.Profile.ProfileName);

        return current with
        {
            CapturedAtUtc = uiState?.LastSnapshotCapturedAtUtc,
            UpdatedAtUtc = now,
            Version = current.Version + 1,
            Source = DomainSnapshotSource.UiProjection,
            Navigation = current.Navigation with
            {
                Status = isTransitioning ? NavigationStatus.InProgress : NavigationStatus.Idle,
                IsTransitioning = isTransitioning,
                UpdatedAtUtc = now
            },
            Combat = current.Combat with
            {
                Status = CombatStatus.Idle,
                OffensiveActionsActive = false,
                DefensivePostureActive = false,
                UpdatedAtUtc = now
            },
            Threat = current.Threat with
            {
                Severity = ThreatSeverity.Unknown,
                IsSafe = null,
                Signals = warnings
            },
            Target = current.Target with
            {
                HasActiveTarget = false,
                Status = TargetingStatus.None,
                UpdatedAtUtc = now
            },
            Companions = current.Companions with
            {
                Status = CompanionStatus.Unknown,
                UpdatedAtUtc = now
            },
            Resources = current.Resources with
            {
                IsDegraded = uiState?.LastRefreshError is not null,
                IsCritical = snapshot.Runtime.CurrentStatus == SessionStatus.Faulted,
                UpdatedAtUtc = now
            },
            Location = current.Location with
            {
                ContextLabel = contextLabel,
                IsUnknown = string.IsNullOrWhiteSpace(contextLabel),
                Confidence = string.IsNullOrWhiteSpace(contextLabel) ? LocationConfidence.Unknown : LocationConfidence.Low,
                UpdatedAtUtc = now
            },
            Warnings = warnings
        };
    }

    public SessionDomainState ProjectRefreshFailure(
        SessionDomainState current,
        Exception exception,
        DateTimeOffset now)
    {
        var warning = $"UI refresh failed: {exception.Message}";
        var warnings = current.Warnings
            .Concat([warning])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return current with
        {
            UpdatedAtUtc = now,
            Version = current.Version + 1,
            Source = DomainSnapshotSource.UiRefreshFailure,
            Navigation = current.Navigation with
            {
                Status = NavigationStatus.Unknown,
                IsTransitioning = false,
                UpdatedAtUtc = now
            },
            Resources = current.Resources with
            {
                IsDegraded = true,
                UpdatedAtUtc = now
            },
            Location = current.Location with
            {
                Confidence = LocationConfidence.Unknown,
                UpdatedAtUtc = now
            },
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> BuildWarnings(
        SessionSnapshot snapshot,
        SessionUiState? uiState,
        DesktopSessionAttachment? attachment)
    {
        var warnings = new List<string>();

        if (attachment is null)
        {
            warnings.Add("No desktop attachment was available during domain projection.");
        }

        if (uiState is null)
        {
            warnings.Add("No UI state was available during domain projection.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
            {
                warnings.Add("No raw UI snapshot is available.");
            }

            if (uiState.ProjectedTree is null)
            {
                warnings.Add("No projected UI tree is available.");
            }

            if (!string.IsNullOrWhiteSpace(uiState.LastRefreshError))
            {
                warnings.Add($"Last UI refresh error: {uiState.LastRefreshError}");
            }
        }

        if (snapshot.Runtime.CurrentStatus == SessionStatus.Faulted && !string.IsNullOrWhiteSpace(snapshot.Runtime.LastError))
        {
            warnings.Add($"Session runtime faulted: {snapshot.Runtime.LastError}");
        }

        return warnings.ToArray();
    }
}
