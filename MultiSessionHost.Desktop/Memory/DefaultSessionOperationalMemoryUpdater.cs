using System.Globalization;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Memory;

public sealed class DefaultSessionOperationalMemoryUpdater : ISessionOperationalMemoryUpdater
{
    private readonly SessionHostOptions _options;

    public DefaultSessionOperationalMemoryUpdater(SessionHostOptions options)
    {
        _options = options;
    }

    public ValueTask<SessionOperationalMemoryUpdateResult> UpdateAsync(
        SessionOperationalMemoryUpdateContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.OperationalMemory.EnableOperationalMemory)
        {
            return ValueTask.FromResult(new SessionOperationalMemoryUpdateResult(
                context.PreviousSnapshot,
                [],
                ["Operational memory is disabled by configuration."],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["updateSkipped"] = "true",
                    ["skipReason"] = "operational-memory-disabled"
                }));
        }

        var previous = context.PreviousSnapshot ?? SessionOperationalMemorySnapshot.Empty(context.SessionId, context.Now);
        var records = new List<MemoryObservationRecord>();
        var warnings = new List<string>();
        var worksites = previous.KnownWorksites.ToDictionary(static item => item.WorksiteKey, StringComparer.OrdinalIgnoreCase);
        var risks = previous.RecentRiskObservations.ToDictionary(static item => item.ObservationId, StringComparer.OrdinalIgnoreCase);
        var presences = previous.RecentPresenceObservations.ToDictionary(static item => item.ObservationId, StringComparer.OrdinalIgnoreCase);
        var timings = previous.RecentTimingObservations.ToDictionary(static item => item.TimingKey, StringComparer.OrdinalIgnoreCase);
        var outcomes = previous.RecentOutcomeObservations.ToDictionary(static item => item.OutcomeId, StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, string>(previous.Metadata, StringComparer.Ordinal)
        {
            ["lastUpdatedAtUtc"] = context.Now.ToString("O")
        };

        ProjectWorksites(context, worksites, records);
        ProjectRisk(context, risks, records);
        ProjectPresence(context, worksites, presences, records);
        ProjectTiming(context, timings, records, metadata);
        ProjectOutcomes(context, worksites, outcomes, records);

        var staleAfter = TimeSpan.FromMinutes(_options.OperationalMemory.StaleAfterMinutes);
        var staleEnabled = staleAfter > TimeSpan.Zero;
        var boundedWorksites = worksites.Values
            .Select(item => item with { IsStale = staleEnabled && context.Now - item.LastObservedAtUtc > staleAfter })
            .OrderByDescending(static item => item.LastObservedAtUtc)
            .ThenBy(static item => item.WorksiteKey, StringComparer.OrdinalIgnoreCase)
            .Take(_options.OperationalMemory.MaxWorksitesPerSession)
            .ToArray();
        var boundedRisks = risks.Values
            .Select(item => item with { IsStale = staleEnabled && context.Now - item.LastObservedAtUtc > staleAfter })
            .OrderByDescending(static item => item.LastObservedAtUtc)
            .ThenBy(static item => item.ObservationId, StringComparer.OrdinalIgnoreCase)
            .Take(_options.OperationalMemory.MaxRiskObservationsPerSession)
            .ToArray();
        var boundedPresences = presences.Values
            .Select(item => item with { IsStale = staleEnabled && context.Now - item.LastObservedAtUtc > staleAfter })
            .OrderByDescending(static item => item.LastObservedAtUtc)
            .ThenBy(static item => item.ObservationId, StringComparer.OrdinalIgnoreCase)
            .Take(_options.OperationalMemory.MaxPresenceObservationsPerSession)
            .ToArray();
        var boundedTimings = timings.Values
            .Select(item => item with { IsStale = staleEnabled && context.Now - item.LastObservedAtUtc > staleAfter })
            .OrderByDescending(static item => item.LastObservedAtUtc)
            .ThenBy(static item => item.TimingKey, StringComparer.OrdinalIgnoreCase)
            .Take(_options.OperationalMemory.MaxTimingObservationsPerSession)
            .ToArray();
        var boundedOutcomes = outcomes.Values
            .Select(item => item with { IsStale = staleEnabled && context.Now - item.ObservedAtUtc > staleAfter })
            .OrderByDescending(static item => item.ObservedAtUtc)
            .ThenBy(static item => item.OutcomeId, StringComparer.OrdinalIgnoreCase)
            .Take(_options.OperationalMemory.MaxOutcomeObservationsPerSession)
            .ToArray();

        var summary = new SessionOperationalMemorySummary(
            boundedWorksites.Length,
            boundedRisks.Count(static item => !item.IsStale),
            boundedPresences.Count(static item => !item.IsStale),
            boundedTimings.Length,
            boundedOutcomes.Length,
            context.Now,
            boundedRisks.Select(static item => item.Severity).DefaultIfEmpty(RiskSeverity.Unknown).Max(),
            boundedOutcomes.FirstOrDefault()?.ResultKind);

        var snapshot = new SessionOperationalMemorySnapshot(
            context.SessionId,
            previous.CapturedAtUtc,
            context.Now,
            summary,
            boundedWorksites,
            boundedRisks,
            boundedPresences,
            boundedTimings,
            boundedOutcomes,
            warnings,
            metadata);

        return ValueTask.FromResult(new SessionOperationalMemoryUpdateResult(
            snapshot,
            records,
            warnings,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["addedObservationRecordCount"] = records.Count.ToString(CultureInfo.InvariantCulture)
            }));
    }

    private static void ProjectWorksites(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, WorksiteObservation> worksites,
        ICollection<MemoryObservationRecord> records)
    {
        foreach (var directive in context.DecisionPlan?.Directives ?? [])
        {
            if (directive.DirectiveKind is not (DecisionDirectiveKind.SelectSite or DecisionDirectiveKind.Navigate))
            {
                continue;
            }

            if (!TryResolveWorksite(directive.TargetId, directive.TargetLabel, directive.Metadata, out var key, out var label))
            {
                continue;
            }

            UpsertWorksite(
                context,
                worksites,
                records,
                key,
                label,
                selectedAt: directive.DirectiveKind == DecisionDirectiveKind.SelectSite ? context.Now : null,
                arrivedAt: null,
                tags: [directive.DirectiveKind.ToString()],
                metadata: new Dictionary<string, string>(directive.Metadata, StringComparer.Ordinal)
                {
                    ["sourceDirectiveKind"] = directive.DirectiveKind.ToString(),
                    ["sourcePolicy"] = directive.SourcePolicy
                });
        }

        if (context.DomainState is { Location.IsUnknown: false } domainState &&
            !string.IsNullOrWhiteSpace(domainState.Location.ContextLabel))
        {
            var key = BuildKey("worksite", domainState.Location.ContextLabel);
            UpsertWorksite(
                context,
                worksites,
                records,
                key,
                domainState.Location.ContextLabel,
                selectedAt: null,
                arrivedAt: domainState.Location.ArrivedAtUtc ?? (domainState.Navigation.Status == NavigationStatus.Idle ? context.Now : null),
                tags: ["location"],
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source"] = "domain-location",
                    ["locationConfidence"] = domainState.Location.Confidence.ToString()
                });
        }

        if (context.ActivitySnapshot?.CurrentState is SessionActivityStateKind.SelectingWorksite &&
            context.DecisionPlan?.Directives.FirstOrDefault(static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite) is { } selectDirective &&
            TryResolveWorksite(selectDirective.TargetId, selectDirective.TargetLabel, selectDirective.Metadata, out var selectedKey, out var selectedLabel))
        {
            UpsertWorksite(
                context,
                worksites,
                records,
                selectedKey,
                selectedLabel,
                context.Now,
                arrivedAt: null,
                tags: ["activity-selection"],
                metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["activityState"] = context.ActivitySnapshot.CurrentState.ToString() });
        }
    }

    private static void ProjectRisk(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, RiskObservation> risks,
        ICollection<MemoryObservationRecord> records)
    {
        foreach (var entity in context.RiskAssessment?.Entities ?? [])
        {
            var id = BuildKey("risk", entity.CandidateId);
            var metadata = new Dictionary<string, string>(entity.Metadata, StringComparer.Ordinal)
            {
                ["source"] = entity.Source.ToString(),
                ["disposition"] = entity.Disposition.ToString(),
                ["entityType"] = entity.Type,
                ["priority"] = entity.Priority.ToString(CultureInfo.InvariantCulture)
            };

            risks[id] = risks.TryGetValue(id, out var existing)
                ? existing with
                {
                    EntityKey = entity.CandidateId,
                    EntityLabel = entity.Name,
                    SourceKey = entity.Source.ToString(),
                    SourceLabel = entity.Type,
                    Severity = entity.Severity,
                    SuggestedPolicy = entity.SuggestedPolicy,
                    RuleName = entity.MatchedRuleName,
                    LastObservedAtUtc = context.Now,
                    Count = existing.Count + 1,
                    LastKnownConfidence = entity.Confidence,
                    Metadata = metadata
                }
                : new RiskObservation(
                    id,
                    entity.CandidateId,
                    entity.Name,
                    entity.Source.ToString(),
                    entity.Type,
                    entity.Severity,
                    entity.SuggestedPolicy,
                    entity.MatchedRuleName,
                    context.Now,
                    context.Now,
                    1,
                    entity.Confidence,
                    IsStale: false,
                    metadata);

            records.Add(CreateRecord(context, MemoryObservationCategory.Risk, id, "risk-assessment", entity.Name, metadata));
        }
    }

    private static void ProjectPresence(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, WorksiteObservation> worksites,
        IDictionary<string, PresenceObservation> presences,
        ICollection<MemoryObservationRecord> records)
    {
        var activePresenceLabels = new List<string>();

        foreach (var entity in context.SemanticExtraction?.PresenceEntities ?? [])
        {
            var label = string.IsNullOrWhiteSpace(entity.Label) ? entity.NodeId : entity.Label!;
            var id = BuildKey("presence", $"{entity.Kind}:{label}");
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "semantic-presence",
                ["nodeId"] = entity.NodeId,
                ["kind"] = entity.Kind.ToString()
            };

            if (entity.Count is not null)
            {
                metadata["count"] = entity.Count.Value.ToString(CultureInfo.InvariantCulture);
            }

            presences[id] = presences.TryGetValue(id, out var existing)
                ? existing with
                {
                    EntityKey = entity.NodeId,
                    EntityLabel = entity.Label,
                    EntityType = entity.Kind.ToString(),
                    Status = entity.Status,
                    LastObservedAtUtc = context.Now,
                    Count = existing.Count + 1,
                    LastKnownConfidence = ConfidenceToDouble(entity.Confidence),
                    Metadata = metadata
                }
                : new PresenceObservation(
                    id,
                    entity.NodeId,
                    entity.Label,
                    entity.Kind.ToString(),
                    entity.Status,
                    context.Now,
                    context.Now,
                    1,
                    ConfidenceToDouble(entity.Confidence),
                    IsStale: false,
                    metadata);

            activePresenceLabels.Add(label);
            records.Add(CreateRecord(context, MemoryObservationCategory.Presence, id, "semantic-extraction", label, metadata));
        }

        if (activePresenceLabels.Count == 0)
        {
            return;
        }

        foreach (var worksite in worksites.Values.ToArray())
        {
            worksites[worksite.WorksiteKey] = worksite with
            {
                OccupancySignals = worksite.OccupancySignals
                    .Concat(activePresenceLabels)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .TakeLast(20)
                    .ToArray()
            };
        }
    }

    private static void ProjectTiming(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, TimingObservation> timings,
        ICollection<MemoryObservationRecord> records,
        IDictionary<string, string> snapshotMetadata)
    {
        if (context.DomainState?.Navigation.StartedAtUtc is { } startedAt &&
            context.DomainState.Location.ArrivedAtUtc is { } arrivedAt &&
            arrivedAt >= startedAt)
        {
            UpsertTiming(context, timings, records, "transition-duration:navigation-arrival", "transition-duration", arrivedAt - startedAt);
        }

        var lastTransition = context.ActivitySnapshot?.LastTransitionAtUtc;
        var previousTransitionValue = context.PreviousSnapshot?.Metadata.TryGetValue("lastActivityTransitionAtUtc", out var value) == true
            ? value
            : null;

        if (lastTransition is not null &&
            !string.Equals(previousTransitionValue, lastTransition.Value.ToString("O"), StringComparison.Ordinal))
        {
            snapshotMetadata["lastActivityTransitionAtUtc"] = lastTransition.Value.ToString("O");

            var recentHistory = context.ActivitySnapshot!.History.OrderBy(static entry => entry.OccurredAtUtc).TakeLast(2).ToArray();
            if (recentHistory.Length == 2 && recentHistory[1].OccurredAtUtc >= recentHistory[0].OccurredAtUtc)
            {
                var kind = ResolveTimingKind(recentHistory[0].ToState);
                var key = BuildKey("timing", $"{recentHistory[0].ToState}->{recentHistory[1].ToState}");
                UpsertTiming(context, timings, records, key, kind, recentHistory[1].OccurredAtUtc - recentHistory[0].OccurredAtUtc);
            }
        }
    }

    private static void ProjectOutcomes(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, WorksiteObservation> worksites,
        IDictionary<string, OutcomeObservation> outcomes,
        ICollection<MemoryObservationRecord> records)
    {
        if (context.ExecutionResult is null)
        {
            var planStatusOutcome = context.DecisionPlan?.PlanStatus switch
            {
                DecisionPlanStatus.Blocked => "deferred",
                DecisionPlanStatus.Aborting => "aborted",
                DecisionPlanStatus.Idle => "no-op",
                _ => null
            };

            if (planStatusOutcome is null)
            {
                return;
            }

            var syntheticId = BuildKey("outcome", $"plan:{context.DecisionPlan!.PlannedAtUtc:O}:{context.DecisionPlan.PlanStatus}");
            if (!outcomes.ContainsKey(syntheticId))
            {
                outcomes[syntheticId] = new OutcomeObservation(
                    syntheticId,
                    RelatedWorksiteKey: null,
                    RelatedDirectiveKind: null,
                    context.ActivitySnapshot?.CurrentState.ToString(),
                    planStatusOutcome,
                    context.Now,
                    $"Decision plan status was {context.DecisionPlan.PlanStatus}.",
                    IsStale: false,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "decision-plan" });
                records.Add(CreateRecord(context, MemoryObservationCategory.Outcome, syntheticId, "decision-plan", planStatusOutcome, outcomes[syntheticId].Metadata));
            }

            return;
        }

        var relatedDirective = context.ExecutionResult.DirectiveResults.FirstOrDefault(static result =>
            result.DirectiveKind is DecisionDirectiveKind.SelectSite or DecisionDirectiveKind.Navigate);
        var relatedWorksiteKey = TryResolveExecutionWorksite(context, relatedDirective, out var worksiteKey) ? worksiteKey : null;
        var resultKind = MapExecutionStatus(context.ExecutionResult.ExecutionStatus);
        var outcomeId = BuildKey(
            "outcome",
            $"{context.ExecutionResult.PlanFingerprint}:{context.ExecutionResult.ExecutedAtUtc:O}:{context.ExecutionResult.ExecutionStatus}");

        if (outcomes.ContainsKey(outcomeId))
        {
            return;
        }

        var metadata = new Dictionary<string, string>(context.ExecutionResult.Metadata, StringComparer.Ordinal)
        {
            ["source"] = "decision-execution",
            ["executionStatus"] = context.ExecutionResult.ExecutionStatus.ToString(),
            ["wasAutoExecuted"] = context.ExecutionResult.WasAutoExecuted.ToString(CultureInfo.InvariantCulture)
        };

        outcomes[outcomeId] = new OutcomeObservation(
            outcomeId,
            relatedWorksiteKey,
            relatedDirective?.DirectiveKind.ToString(),
            context.ActivitySnapshot?.CurrentState.ToString(),
            resultKind,
            context.ExecutionResult.ExecutedAtUtc,
            context.ExecutionResult.FailureReason ?? relatedDirective?.Message,
            IsStale: false,
            metadata);
        records.Add(CreateRecord(context, MemoryObservationCategory.Outcome, outcomeId, "decision-execution", resultKind, metadata));

        if (relatedWorksiteKey is not null && worksites.TryGetValue(relatedWorksiteKey, out var worksite))
        {
            worksites[relatedWorksiteKey] = worksite with
            {
                LastOutcome = resultKind,
                SuccessCount = resultKind == "success" ? worksite.SuccessCount + 1 : worksite.SuccessCount,
                FailureCount = resultKind is "failure" or "aborted" or "withdrawn" ? worksite.FailureCount + 1 : worksite.FailureCount,
                LastObservedAtUtc = context.Now
            };
        }
    }

    private static void UpsertWorksite(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, WorksiteObservation> worksites,
        ICollection<MemoryObservationRecord> records,
        string key,
        string? label,
        DateTimeOffset? selectedAt,
        DateTimeOffset? arrivedAt,
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> metadata)
    {
        var topSeverity = context.RiskAssessment?.Summary.HighestSeverity ?? RiskSeverity.Unknown;
        worksites[key] = worksites.TryGetValue(key, out var existing)
            ? existing with
            {
                WorksiteLabel = label ?? existing.WorksiteLabel,
                Tags = existing.Tags.Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                LastObservedAtUtc = context.Now,
                LastSelectedAtUtc = selectedAt ?? existing.LastSelectedAtUtc,
                LastArrivedAtUtc = arrivedAt ?? existing.LastArrivedAtUtc,
                LastObservedRiskSeverity = MaxSeverity(existing.LastObservedRiskSeverity, topSeverity),
                VisitCount = arrivedAt is not null && existing.LastArrivedAtUtc != arrivedAt ? existing.VisitCount + 1 : existing.VisitCount,
                LastKnownConfidence = context.DomainState is null ? existing.LastKnownConfidence : ConfidenceToDouble(context.DomainState.Location.Confidence),
                Metadata = metadata
            }
            : new WorksiteObservation(
                key,
                label,
                tags,
                context.Now,
                context.Now,
                selectedAt,
                arrivedAt,
                LastOutcome: null,
                topSeverity,
                OccupancySignals: [],
                VisitCount: arrivedAt is null ? 0 : 1,
                SuccessCount: 0,
                FailureCount: 0,
                context.DomainState is null ? null : ConfidenceToDouble(context.DomainState.Location.Confidence),
                IsStale: false,
                metadata);

        records.Add(CreateRecord(context, MemoryObservationCategory.Worksite, key, "memory-projector", label, metadata));
    }

    private static void UpsertTiming(
        SessionOperationalMemoryUpdateContext context,
        IDictionary<string, TimingObservation> timings,
        ICollection<MemoryObservationRecord> records,
        string key,
        string kind,
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            return;
        }

        var durationMs = duration.TotalMilliseconds;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind
        };

        timings[key] = timings.TryGetValue(key, out var existing)
            ? existing with
            {
                LastObservedAtUtc = context.Now,
                LastDurationMs = durationMs,
                SampleCount = existing.SampleCount + 1,
                MinDurationMs = Math.Min(existing.MinDurationMs, durationMs),
                MaxDurationMs = Math.Max(existing.MaxDurationMs, durationMs),
                AverageDurationMs = ((existing.AverageDurationMs * existing.SampleCount) + durationMs) / (existing.SampleCount + 1),
                Metadata = metadata
            }
            : new TimingObservation(
                key,
                kind,
                context.Now,
                context.Now,
                durationMs,
                1,
                durationMs,
                durationMs,
                durationMs,
                IsStale: false,
                metadata);

        records.Add(CreateRecord(context, MemoryObservationCategory.Timing, key, "timing-projector", $"{kind}:{durationMs:0}ms", metadata));
    }

    private static bool TryResolveExecutionWorksite(
        SessionOperationalMemoryUpdateContext context,
        DecisionDirectiveExecutionResult? directiveResult,
        out string key)
    {
        key = string.Empty;

        if (directiveResult is not null &&
            context.DecisionPlan?.Directives.FirstOrDefault(directive => directive.DirectiveId == directiveResult.DirectiveId) is { } directive &&
            TryResolveWorksite(directive.TargetId, directive.TargetLabel, directive.Metadata, out key, out _))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.DomainState?.Location.ContextLabel))
        {
            key = BuildKey("worksite", context.DomainState.Location.ContextLabel);
            return true;
        }

        return false;
    }

    private static bool TryResolveWorksite(
        string? targetId,
        string? targetLabel,
        IReadOnlyDictionary<string, string> metadata,
        out string key,
        out string? label)
    {
        label = FirstNonEmpty(
            targetLabel,
            Get(metadata, "worksiteLabel"),
            Get(metadata, "siteLabel"),
            Get(metadata, "targetLabel"),
            Get(metadata, "destinationLabel"));
        var rawKey = FirstNonEmpty(
            Get(metadata, "worksiteKey"),
            Get(metadata, "siteKey"),
            label,
            targetId);

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            key = string.Empty;
            return false;
        }

        key = BuildKey("worksite", rawKey);
        return true;
    }

    private static MemoryObservationRecord CreateRecord(
        SessionOperationalMemoryUpdateContext context,
        MemoryObservationCategory category,
        string key,
        string source,
        string? summary,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            $"{category}:{key}:{context.Now.UtcTicks.ToString(CultureInfo.InvariantCulture)}",
            context.SessionId,
            category,
            key,
            context.Now,
            source,
            summary,
            metadata);

    private static string ResolveTimingKind(SessionActivityStateKind state) =>
        state switch
        {
            SessionActivityStateKind.Traveling => "transition-duration",
            SessionActivityStateKind.Arriving => "arrival-delay",
            SessionActivityStateKind.Hiding or SessionActivityStateKind.Recovering => "cooldown-like-delay",
            SessionActivityStateKind.WaitingForSpawn => "wait-window",
            _ => "transition-duration"
        };

    private static string MapExecutionStatus(DecisionPlanExecutionStatus status) =>
        status switch
        {
            DecisionPlanExecutionStatus.Succeeded => "success",
            DecisionPlanExecutionStatus.Failed => "failure",
            DecisionPlanExecutionStatus.Aborted => "aborted",
            DecisionPlanExecutionStatus.Deferred or DecisionPlanExecutionStatus.Blocked or DecisionPlanExecutionStatus.Skipped => "deferred",
            DecisionPlanExecutionStatus.NoOp => "no-op",
            _ => "no-op"
        };

    private static RiskSeverity MaxSeverity(RiskSeverity left, RiskSeverity right) =>
        left >= right ? left : right;

    private static string BuildKey(string prefix, string value) =>
        $"{prefix}:{value.Trim().ToLowerInvariant()}";

    private static string? Get(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static double? ConfidenceToDouble(DetectionConfidence confidence) =>
        confidence switch
        {
            DetectionConfidence.Low => 0.25,
            DetectionConfidence.Medium => 0.5,
            DetectionConfidence.High => 0.9,
            _ => null
        };

    private static double? ConfidenceToDouble(LocationConfidence confidence) =>
        confidence switch
        {
            LocationConfidence.Low => 0.25,
            LocationConfidence.Medium => 0.5,
            LocationConfidence.High => 0.9,
            _ => null
        };
}
