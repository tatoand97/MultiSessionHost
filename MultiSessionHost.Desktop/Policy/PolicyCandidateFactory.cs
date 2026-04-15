using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

internal static class PolicyCandidateFactory
{
    public static SiteSelectionCandidate CreateSiteSelection(PolicyEvaluationContext context, string siteLabel, string siteType)
    {
        var common = CreateCommon(context);
        var metadata = With(common.Metadata, ("siteLabel", siteLabel), ("siteType", siteType), ("locationConfidence", context.SessionDomainState.Location.Confidence.ToString()));
        return new SiteSelectionCandidate("site:" + siteLabel, siteLabel, siteType, Tags(context.SessionSnapshot.Definition.Tags), common.ThreatSeverity, common.RiskSeverity, common.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, common.Confidence, common.Metrics, metadata);
    }

    public static IReadOnlyList<ThreatResponseCandidate> CreateThreatResponse(PolicyEvaluationContext context)
    {
        var common = CreateCommon(context);
        var topThreat = PolicyHelpers.GetTopThreat(context.RiskAssessmentResult);
        var label = topThreat?.Name ?? context.SessionDomainState.Threat.TopEntityLabel ?? context.SessionDomainState.Target.PrimaryTargetLabel ?? context.SessionId.Value;
        var type = topThreat?.Type ?? context.SessionDomainState.Threat.TopEntityType ?? "Threat";
        var candidateId = topThreat?.CandidateId ?? context.SessionDomainState.Target.PrimaryTargetId ?? "threat:" + label;
        var tags = topThreat?.Tags ?? [];
        var metadata = With(common.Metadata, ("entityType", type), ("matchedRiskRule", topThreat?.MatchedRuleName));
        return [new ThreatResponseCandidate(candidateId, label, type, tags, common.ThreatSeverity, common.RiskSeverity, common.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, topThreat?.Confidence, common.Metrics, metadata)];
    }

    public static IReadOnlyList<TargetPriorityCandidate> CreateTargetPriority(PolicyEvaluationContext context)
    {
        var common = CreateCommon(context);
        var candidates = new List<TargetPriorityCandidate>();

        foreach (var threat in context.RiskAssessmentResult?.Entities.Where(static entity => entity.Disposition == RiskDisposition.Threat) ?? [])
        {
            candidates.Add(new TargetPriorityCandidate(threat.CandidateId, threat.Name, threat.Type, threat.Tags, common.ThreatSeverity, threat.Severity, threat.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, threat.Confidence, common.Metrics, With(common.Metadata, ("entityPriority", threat.Priority.ToString()), ("matchedRiskRule", threat.MatchedRuleName))));
        }

        if (context.SessionDomainState.Target.HasActiveTarget)
        {
            candidates.Add(new TargetPriorityCandidate(context.SessionDomainState.Target.PrimaryTargetId ?? "active-target", context.SessionDomainState.Target.PrimaryTargetLabel, context.SessionDomainState.Target.Status.ToString(), [], common.ThreatSeverity, common.RiskSeverity, RiskPolicySuggestion.None, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, common.Confidence, common.Metrics, With(common.Metadata, ("targetStatus", context.SessionDomainState.Target.Status.ToString()))));
        }

        foreach (var target in context.UiSemanticExtractionResult?.Targets ?? [])
        {
            candidates.Add(new TargetPriorityCandidate(target.NodeId, target.Label, target.Kind.ToString(), [], common.ThreatSeverity, common.RiskSeverity, RiskPolicySuggestion.None, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, ToConfidence(target.Confidence), common.Metrics, With(common.Metadata, ("selected", target.Selected.ToString()), ("active", target.Active.ToString()), ("focused", target.Focused.ToString()), ("targetKind", target.Kind.ToString()))));
        }

        return candidates
            .OrderByDescending(static candidate => candidate.RiskSeverity)
            .ThenByDescending(static candidate => candidate.Confidence ?? 0)
            .ThenBy(static candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ResourceUsageCandidate CreateResourceUsage(PolicyEvaluationContext context)
    {
        var common = CreateCommon(context);
        return new ResourceUsageCandidate("resources", "resources", "ResourceSet", [], common.ThreatSeverity, common.RiskSeverity, common.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, common.Confidence, common.Metrics, common.Metadata);
    }

    public static TransitCandidate CreateTransit(PolicyEvaluationContext context)
    {
        var common = CreateCommon(context);
        var navigation = context.SessionDomainState.Navigation;
        return new TransitCandidate("transit", navigation.DestinationLabel, navigation.RouteLabel ?? "Navigation", [], common.ThreatSeverity, common.RiskSeverity, common.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, common.Confidence, common.Metrics, With(common.Metadata, ("destination", navigation.DestinationLabel), ("route", navigation.RouteLabel)));
    }

    public static AbortCandidate CreateAbort(PolicyEvaluationContext context)
    {
        var common = CreateCommon(context);
        var label = context.RiskAssessmentResult?.Summary.TopCandidateName ?? context.SessionDomainState.Threat.TopEntityLabel ?? context.SessionId.Value;
        var type = context.RiskAssessmentResult?.Summary.TopCandidateType ?? context.SessionDomainState.Threat.TopEntityType ?? "Session";
        var candidateId = context.RiskAssessmentResult?.Summary.TopCandidateId ?? context.SessionDomainState.Target.PrimaryTargetId ?? context.SessionId.Value;
        return new AbortCandidate(candidateId, label, type, [], common.ThreatSeverity, common.RiskSeverity, common.SuggestedPolicy, common.SessionStatus, common.NavigationStatus, common.IsTransitioning, common.HasDestination, common.IsNavigationIdle, common.IsActivityIdle, common.HasActiveTarget, common.ResourceCritical, common.ResourceDegraded, common.DefensivePostureActive, common.ProgressPercent, common.ResourcePercent, common.WarningCount, common.UnknownCount, common.AvailableCount, common.Confidence, common.Metrics, common.Metadata);
    }

    private static CommonCandidateValues CreateCommon(PolicyEvaluationContext context)
    {
        var domain = context.SessionDomainState;
        var risk = context.RiskAssessmentResult;
        var resourcePercent = new[] { domain.Resources.HealthPercent, domain.Resources.CapacityPercent, domain.Resources.EnergyPercent }
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty()
            .Min();
        double? nullableResourcePercent = new[] { domain.Resources.HealthPercent, domain.Resources.CapacityPercent, domain.Resources.EnergyPercent }.Any(static value => value.HasValue)
            ? resourcePercent
            : null;
        var suggestedPolicy = risk?.Summary.TopSuggestedPolicy ?? ParsePolicy(domain.Threat.TopSuggestedPolicy);
        var metadata = PolicyHelpers.Metadata(
            ("severity", domain.Threat.Severity.ToString()),
            ("suggestedPolicy", suggestedPolicy.ToString()),
            ("navigationStatus", domain.Navigation.Status.ToString()),
            ("progressPercent", domain.Navigation.ProgressPercent?.ToString("0.##")),
            ("lowestResourcePercent", nullableResourcePercent?.ToString("0.##")),
            ("warningCount", domain.Warnings.Count.ToString()));
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (domain.Navigation.ProgressPercent is { } progressPercent)
        {
            metrics["progressPercent"] = progressPercent;
        }

        if (nullableResourcePercent is { } lowestResource)
        {
            metrics["resourcePercent"] = lowestResource;
        }

        if (domain.Threat.UnknownCount is { } unknownCount)
        {
            metrics["unknownCount"] = unknownCount;
        }

        if (domain.Resources.AvailableChargeCount is { } availableCount)
        {
            metrics["availableCount"] = availableCount;
        }

        return new CommonCandidateValues(
            domain.Threat.Severity,
            risk?.Summary.HighestSeverity ?? RiskSeverity.Unknown,
            suggestedPolicy,
            context.SessionSnapshot.Runtime.CurrentStatus,
            domain.Navigation.Status,
            domain.Navigation.IsTransitioning,
            !string.IsNullOrWhiteSpace(domain.Navigation.DestinationLabel),
            domain.Navigation.Status is NavigationStatus.Idle or NavigationStatus.Unknown,
            domain.Combat.Status is CombatStatus.Idle or CombatStatus.Unknown,
            domain.Target.HasActiveTarget,
            domain.Resources.IsCritical,
            domain.Resources.IsDegraded,
            domain.Combat.DefensivePostureActive,
            domain.Navigation.ProgressPercent,
            nullableResourcePercent,
            domain.Warnings.Count,
            risk?.Summary.UnknownCount ?? domain.Threat.UnknownCount,
            domain.Resources.AvailableChargeCount,
            null,
            metrics,
            metadata);
    }

    private static RiskPolicySuggestion ParsePolicy(string? value) =>
        Enum.TryParse<RiskPolicySuggestion>(value, ignoreCase: true, out var policy)
            ? policy
            : RiskPolicySuggestion.None;

    private static double ToConfidence(DetectionConfidence confidence) =>
        confidence switch
        {
            DetectionConfidence.High => 1,
            DetectionConfidence.Medium => 0.66,
            DetectionConfidence.Low => 0.33,
            _ => 0
        };

    private static IReadOnlyList<string> Tags(IEnumerable<string> tags) =>
        tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)).Select(static tag => tag.Trim()).ToArray();

    private static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> source, params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string>(source, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private sealed record CommonCandidateValues(
        ThreatSeverity ThreatSeverity,
        RiskSeverity RiskSeverity,
        RiskPolicySuggestion SuggestedPolicy,
        SessionStatus SessionStatus,
        NavigationStatus NavigationStatus,
        bool IsTransitioning,
        bool HasDestination,
        bool IsNavigationIdle,
        bool IsActivityIdle,
        bool HasActiveTarget,
        bool ResourceCritical,
        bool ResourceDegraded,
        bool DefensivePostureActive,
        double? ProgressPercent,
        double? ResourcePercent,
        int? WarningCount,
        int? UnknownCount,
        int? AvailableCount,
        double? Confidence,
        IReadOnlyDictionary<string, double> Metrics,
        IReadOnlyDictionary<string, string> Metadata);
}
