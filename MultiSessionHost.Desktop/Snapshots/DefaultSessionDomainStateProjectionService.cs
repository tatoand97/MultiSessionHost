using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Risk;
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
        UiSemanticExtractionResult? semanticExtraction,
        RiskAssessmentResult? riskAssessment,
        DateTimeOffset now)
    {
        var warnings = BuildWarnings(snapshot, uiState, attachment, semanticExtraction, riskAssessment);
        var hasPlannedWork = uiState?.PlannedWorkItems.Count > 0;
        var semanticTransit = semanticExtraction?.TransitStates
            .OrderByDescending(static state => state.Confidence)
            .FirstOrDefault(static state => state.Status is TransitStatus.InProgress or TransitStatus.Blocked);
        var isTransitioning =
            snapshot.Runtime.CurrentStatus is SessionStatus.Starting or SessionStatus.Stopping ||
            snapshot.PendingWorkItems > 0 ||
            snapshot.Runtime.InFlightWorkItems > 0 ||
            hasPlannedWork ||
            semanticTransit?.Status == TransitStatus.InProgress;
        var contextLabel = DesktopTargetMetadata.GetValue(
            context.Target.Metadata,
            DesktopTargetMetadata.UiSource,
            context.Profile.ProfileName);
        var primaryTarget = semanticExtraction?.Targets
            .OrderByDescending(static target => target.Confidence)
            .ThenByDescending(static target => target.Selected)
            .ThenByDescending(static target => target.Active)
            .FirstOrDefault();
        var semanticAlert = semanticExtraction?.Alerts
            .Where(static alert => alert.Visible)
            .OrderByDescending(static alert => alert.Severity)
            .ThenByDescending(static alert => alert.Confidence)
            .FirstOrDefault();
        var strongestThreat = riskAssessment?.Entities
            .Where(static entity => entity.Disposition == RiskDisposition.Threat)
            .OrderByDescending(static entity => entity.Severity)
            .ThenByDescending(static entity => entity.Priority)
            .ThenByDescending(static entity => entity.Confidence)
            .FirstOrDefault();
        var topRiskEntity = riskAssessment?.Entities
            .OrderByDescending(static entity => entity.Priority)
            .ThenByDescending(static entity => entity.Severity)
            .ThenByDescending(static entity => entity.Confidence)
            .FirstOrDefault();
        var strongestResource = semanticExtraction?.Resources
            .OrderByDescending(static resource => resource.Critical)
            .ThenByDescending(static resource => resource.Degraded)
            .ThenByDescending(static resource => resource.Confidence)
            .FirstOrDefault();
        var presence = semanticExtraction?.PresenceEntities
            .OrderByDescending(static entity => entity.Confidence)
            .FirstOrDefault();
        var semanticContextLabel = semanticExtraction?.Lists
            .Where(static list => !string.IsNullOrWhiteSpace(list.Label))
            .OrderByDescending(static list => list.Confidence)
            .FirstOrDefault(static list => list.Kind is ListKind.Presence or ListKind.Navigation)?.Label;

        return current with
        {
            CapturedAtUtc = uiState?.LastSnapshotCapturedAtUtc,
            UpdatedAtUtc = now,
            Version = current.Version + 1,
            Source = DomainSnapshotSource.UiProjection,
            Navigation = current.Navigation with
            {
                Status = semanticTransit?.Status == TransitStatus.Blocked
                    ? NavigationStatus.Blocked
                    : isTransitioning ? NavigationStatus.InProgress : NavigationStatus.Idle,
                IsTransitioning = isTransitioning,
                DestinationLabel = semanticTransit?.Label ?? current.Navigation.DestinationLabel,
                ProgressPercent = semanticTransit?.ProgressPercent,
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
                Severity = MapThreatSeverity(strongestThreat?.Severity, semanticAlert?.Severity),
                UnknownCount = riskAssessment?.Summary.UnknownCount ?? presence?.Count,
                NeutralCount = riskAssessment?.Summary.SafeCount ?? current.Threat.NeutralCount,
                HostileCount = riskAssessment?.Summary.ThreatCount ?? current.Threat.HostileCount,
                IsSafe = riskAssessment is not null
                    ? riskAssessment.Summary.ThreatCount == 0 && riskAssessment.Summary.UnknownCount == 0
                    : semanticAlert is null && (semanticExtraction?.Alerts.Count ?? 0) == 0 ? true : null,
                LastThreatChangedAtUtc = strongestThreat is not null || semanticAlert is not null ? now : current.Threat.LastThreatChangedAtUtc,
                Signals = BuildThreatSignals(warnings, semanticAlert, semanticExtraction, riskAssessment),
                TopSuggestedPolicy = topRiskEntity?.SuggestedPolicy.ToString(),
                TopEntityLabel = topRiskEntity?.Name,
                TopEntityType = topRiskEntity?.Type
            },
            Target = current.Target with
            {
                HasActiveTarget = primaryTarget is not null,
                PrimaryTargetId = primaryTarget?.NodeId,
                PrimaryTargetLabel = primaryTarget?.Label,
                TrackedTargetCount = semanticExtraction?.Targets.Count,
                LockedTargetCount = semanticExtraction?.Targets.Count(static target => target.Active),
                SelectedTargetCount = semanticExtraction?.Targets.Count(static target => target.Selected),
                Status = primaryTarget is null
                    ? TargetingStatus.None
                    : primaryTarget.Active || primaryTarget.Selected ? TargetingStatus.Active : TargetingStatus.Acquiring,
                LastTargetChangedAtUtc = primaryTarget is not null ? now : current.Target.LastTargetChangedAtUtc,
                UpdatedAtUtc = now
            },
            Companions = current.Companions with
            {
                Status = presence?.Count > 0 ? CompanionStatus.Active : CompanionStatus.Unknown,
                AreAvailable = presence?.Count > 0 ? true : null,
                ActiveCount = presence?.Count,
                UpdatedAtUtc = now
            },
            Resources = current.Resources with
            {
                HealthPercent = SelectResourcePercent(semanticExtraction, ResourceKind.Health),
                CapacityPercent = SelectResourcePercent(semanticExtraction, ResourceKind.Capacity),
                EnergyPercent = SelectResourcePercent(semanticExtraction, ResourceKind.Energy),
                AvailableChargeCount = SelectChargeCount(semanticExtraction),
                IsDegraded = uiState?.LastRefreshError is not null || strongestResource?.Degraded == true,
                IsCritical = snapshot.Runtime.CurrentStatus == SessionStatus.Faulted || strongestResource?.Critical == true,
                UpdatedAtUtc = now
            },
            Location = current.Location with
            {
                ContextLabel = contextLabel,
                SubLocationLabel = semanticContextLabel ?? presence?.Label,
                IsUnknown = string.IsNullOrWhiteSpace(contextLabel),
                Confidence = GetLocationConfidence(semanticContextLabel, contextLabel),
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
        DesktopSessionAttachment? attachment,
        UiSemanticExtractionResult? semanticExtraction,
        RiskAssessmentResult? riskAssessment)
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

        if (semanticExtraction is null)
        {
            warnings.Add("No semantic extraction result was available during domain projection.");
        }
        else
        {
            warnings.AddRange(semanticExtraction.Warnings);
        }

        if (riskAssessment is null)
        {
            warnings.Add("No risk assessment result was available during domain projection.");
        }
        else
        {
            warnings.AddRange(riskAssessment.Warnings);
        }

        return warnings.ToArray();
    }

    private static IReadOnlyList<string> BuildThreatSignals(
        IReadOnlyList<string> warnings,
        DetectedAlert? alert,
        UiSemanticExtractionResult? semanticExtraction,
        RiskAssessmentResult? riskAssessment)
    {
        var signals = new List<string>(warnings);

        if (alert is not null)
        {
            signals.Add($"{alert.Severity}: {alert.Message}");
        }

        if (semanticExtraction?.PresenceEntities.Count > 0)
        {
            signals.Add($"Presence entities detected: {semanticExtraction.PresenceEntities.Count}");
        }

        if (riskAssessment?.Summary is { } summary)
        {
            signals.Add($"Risk summary: safe={summary.SafeCount}, unknown={summary.UnknownCount}, threat={summary.ThreatCount}, highest={summary.HighestSeverity}, priority={summary.HighestPriority}, policy={summary.TopSuggestedPolicy}.");
        }

        foreach (var entity in riskAssessment?.Entities.Take(3) ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entity.MatchedRuleName))
            {
                signals.Add($"Risk rule '{entity.MatchedRuleName}' matched '{entity.Name}' with policy {entity.SuggestedPolicy}.");
            }

            foreach (var reason in entity.Reasons.Take(2))
            {
                signals.Add($"Risk reason for '{entity.Name}': {reason}");
            }
        }

        return signals.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static ThreatSeverity MapThreatSeverity(RiskSeverity? riskSeverity, AlertSeverity? alertSeverity) =>
        riskSeverity switch
        {
            RiskSeverity.Low => ThreatSeverity.Low,
            RiskSeverity.Moderate => ThreatSeverity.Moderate,
            RiskSeverity.High => ThreatSeverity.High,
            RiskSeverity.Critical => ThreatSeverity.Critical,
            RiskSeverity.Unknown => MapThreatSeverity(alertSeverity),
            _ => MapThreatSeverity(alertSeverity)
        };

    private static ThreatSeverity MapThreatSeverity(AlertSeverity? alertSeverity) =>
        alertSeverity switch
        {
            AlertSeverity.Info => ThreatSeverity.Low,
            AlertSeverity.Warning => ThreatSeverity.Moderate,
            AlertSeverity.Error => ThreatSeverity.High,
            AlertSeverity.Critical => ThreatSeverity.Critical,
            _ => ThreatSeverity.Unknown
        };

    private static double? SelectResourcePercent(UiSemanticExtractionResult? semanticExtraction, ResourceKind kind) =>
        semanticExtraction?.Resources
            .Where(resource => resource.Kind == kind && resource.Percent is not null)
            .OrderByDescending(static resource => resource.Confidence)
            .Select(static resource => resource.Percent)
            .FirstOrDefault();

    private static int? SelectChargeCount(UiSemanticExtractionResult? semanticExtraction)
    {
        var value = semanticExtraction?.Resources
            .Where(static resource => resource.Kind == ResourceKind.Charge && resource.Value is not null)
            .OrderByDescending(static resource => resource.Confidence)
            .Select(static resource => resource.Value)
            .FirstOrDefault();

        return value is null ? null : Convert.ToInt32(value.Value);
    }

    private static LocationConfidence GetLocationConfidence(string? semanticContextLabel, string? fallbackContextLabel)
    {
        if (!string.IsNullOrWhiteSpace(semanticContextLabel))
        {
            return LocationConfidence.Medium;
        }

        return string.IsNullOrWhiteSpace(fallbackContextLabel) ? LocationConfidence.Unknown : LocationConfidence.Low;
    }
}
