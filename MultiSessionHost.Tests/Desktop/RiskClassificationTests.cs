using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Tests.Desktop;

public sealed class RiskClassificationTests
{
    [Fact]
    public void CandidateBuilder_CreatesCandidatesFromTargetsPresenceAndAlerts()
    {
        var builder = new DefaultRiskCandidateBuilder();
        var semantic = CreateSemanticExtraction();

        var candidates = builder.BuildCandidates(semantic);

        Assert.Contains(candidates, candidate => candidate.Source == RiskEntitySource.Target && candidate.Name.Contains("priority", StringComparison.OrdinalIgnoreCase) && candidate.Tags.Contains("selected", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(candidates, candidate => candidate.Source == RiskEntitySource.Presence && candidate.Tags.Contains("presence", StringComparer.OrdinalIgnoreCase) && candidate.Metadata["count"] == "2");
        Assert.Contains(candidates, candidate => candidate.Source == RiskEntitySource.Alert && candidate.Type == "Critical" && candidate.Tags.Contains("critical", StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classifier_AppliesNameTypeTagMatchesAndFirstMatchWins()
    {
        var classifier = new DefaultRiskClassifier(CreateOptions());
        var rules = new ConfiguredRiskRuleProvider(CreateOptions()).GetActiveRules();
        var candidates = new[]
        {
            Candidate("a", "trusted service", "Warning", ["selected"]),
            Candidate("b", "priority objective", "Item", ["selected"]),
            Candidate("c", "anything", "Warning", ["alert"]),
            Candidate("d", "anything", "Person", ["unknown"])
        };

        var results = classifier.Classify(candidates, rules);

        AssertAssessment(results, "a", RiskDisposition.Safe, RiskPolicySuggestion.Ignore, "safe-name");
        AssertAssessment(results, "b", RiskDisposition.Threat, RiskPolicySuggestion.Prioritize, "priority-name");
        AssertAssessment(results, "c", RiskDisposition.Threat, RiskPolicySuggestion.Withdraw, "warning-type");
        AssertAssessment(results, "d", RiskDisposition.Unknown, RiskPolicySuggestion.Observe, "unknown-tag");
    }

    [Fact]
    public void Classifier_UsesDefaultUnknownFallbackWhenNoRuleMatches()
    {
        var classifier = new DefaultRiskClassifier(CreateOptions());

        var result = classifier.Classify([Candidate("x", "unmatched", "Service", ["presence"])], [])[0];

        Assert.Equal(RiskDisposition.Unknown, result.Disposition);
        Assert.Equal(RiskSeverity.Unknown, result.Severity);
        Assert.Equal(RiskPolicySuggestion.Observe, result.SuggestedPolicy);
        Assert.Null(result.MatchedRuleName);
        Assert.Contains(result.Reasons, reason => reason.Contains("No explicit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pipeline_AggregatesCountsPrioritySeverityAndTopPolicy()
    {
        var options = CreateOptions();
        var sessionId = new SessionId("risk-pipeline");
        var semanticStore = new InMemorySessionSemanticExtractionStore();
        await semanticStore.UpdateAsync(sessionId, CreateSemanticExtraction(sessionId), CancellationToken.None);
        var assessmentStore = new InMemorySessionRiskAssessmentStore();
        var pipeline = new DefaultRiskClassificationPipeline(
            options,
            semanticStore,
            new DefaultRiskCandidateBuilder(),
            new ConfiguredRiskRuleProvider(options),
            new DefaultRiskClassifier(options),
            assessmentStore,
            new FixedClock(DateTimeOffset.Parse("2026-04-15T14:00:00Z")));

        var result = await pipeline.AssessAsync(sessionId, CancellationToken.None);
        var persisted = await assessmentStore.GetLatestAsync(sessionId, CancellationToken.None);

        Assert.Same(result, persisted);
        Assert.Equal(1, result.Summary.SafeCount);
        Assert.True(result.Summary.UnknownCount >= 1);
        Assert.True(result.Summary.ThreatCount >= 2);
        Assert.Equal(RiskSeverity.Critical, result.Summary.HighestSeverity);
        Assert.Equal(900, result.Summary.HighestPriority);
        Assert.Equal(RiskPolicySuggestion.Prioritize, result.Summary.TopSuggestedPolicy);
    }

    [Fact]
    public async Task Store_RemainsSessionIsolated()
    {
        var store = new InMemorySessionRiskAssessmentStore();
        var alpha = RiskAssessmentResult.Empty(new SessionId("alpha"), DateTimeOffset.UtcNow);
        var beta = RiskAssessmentResult.Empty(new SessionId("beta"), DateTimeOffset.UtcNow);

        await store.UpdateAsync(alpha.SessionId, alpha, CancellationToken.None);
        await store.UpdateAsync(beta.SessionId, beta, CancellationToken.None);
        await store.RemoveAsync(beta.SessionId, CancellationToken.None);

        Assert.NotNull(await store.GetLatestAsync(alpha.SessionId, CancellationToken.None));
        Assert.Null(await store.GetLatestAsync(beta.SessionId, CancellationToken.None));
        Assert.Single(await store.GetAllAsync(CancellationToken.None));
    }

    internal static SessionHostOptions CreateOptions() =>
        new()
        {
            Sessions = [new SessionDefinitionOptions { SessionId = "risk", DisplayName = "Risk" }],
            RiskClassification = new RiskClassificationOptions
            {
                EnableRiskClassification = true,
                DefaultUnknownDisposition = RiskDisposition.Unknown,
                DefaultUnknownSeverity = RiskSeverity.Unknown,
                DefaultUnknownPolicy = RiskPolicySuggestion.Observe,
                MaxReturnedEntities = 50,
                RequireExplicitSafeMatch = true,
                Rules =
                [
                    new RiskRuleOptions
                    {
                        RuleName = "safe-name",
                        MatchByName = ["trusted", "safe", "allowed"],
                        NameMatchMode = RiskRuleMatchMode.Contains,
                        Disposition = RiskDisposition.Safe,
                        Severity = RiskSeverity.Low,
                        Priority = 10,
                        SuggestedPolicy = RiskPolicySuggestion.Ignore,
                        Reason = "Configured safe label."
                    },
                    new RiskRuleOptions
                    {
                        RuleName = "priority-name",
                        MatchByName = ["priority"],
                        NameMatchMode = RiskRuleMatchMode.Contains,
                        Disposition = RiskDisposition.Threat,
                        Severity = RiskSeverity.High,
                        Priority = 900,
                        SuggestedPolicy = RiskPolicySuggestion.Prioritize,
                        Reason = "Configured prioritized entity."
                    },
                    new RiskRuleOptions
                    {
                        RuleName = "warning-type",
                        MatchByType = ["Warning", "Critical"],
                        TypeMatchMode = RiskRuleMatchMode.Exact,
                        Disposition = RiskDisposition.Threat,
                        Severity = RiskSeverity.Critical,
                        Priority = 800,
                        SuggestedPolicy = RiskPolicySuggestion.Withdraw,
                        Reason = "Configured warning type."
                    },
                    new RiskRuleOptions
                    {
                        RuleName = "unknown-tag",
                        MatchByTags = ["unknown"],
                        Disposition = RiskDisposition.Unknown,
                        Severity = RiskSeverity.Low,
                        Priority = 100,
                        SuggestedPolicy = RiskPolicySuggestion.Observe,
                        Reason = "Configured unknown tag."
                    }
                ]
            }
        };

    internal static UiSemanticExtractionResult CreateSemanticExtraction() =>
        CreateSemanticExtraction(new SessionId("risk"));

    internal static UiSemanticExtractionResult CreateSemanticExtraction(SessionId sessionId) =>
        new(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T13:55:00Z"),
            Lists: [],
            Targets:
            [
                new DetectedTarget("target-1", "priority objective", Selected: true, Active: false, Focused: false, Count: null, Index: 0, TargetKind.SelectedItem, DetectionConfidence.High),
                new DetectedTarget("target-2", "trusted target", Selected: false, Active: false, Focused: false, Count: null, Index: 1, TargetKind.ActionTarget, DetectionConfidence.Medium)
            ],
            Alerts:
            [
                new DetectedAlert("alert-1", "critical alert", AlertSeverity.Critical, Visible: true, SourceHint: "system", DetectionConfidence.High)
            ],
            TransitStates: [],
            Resources: [],
            Capabilities: [],
            PresenceEntities:
            [
                new DetectedPresenceEntity("presence-1", "unknown presence", Count: 2, Membership: ["unknown"], PresenceEntityKind.Person, Status: "unknown", DetectionConfidence.Medium)
            ],
            Warnings: [],
            ConfidenceSummary: new Dictionary<string, DetectionConfidence>());

    private static RiskCandidate Candidate(string id, string name, string type, IReadOnlyList<string> tags) =>
        new(id, new SessionId("risk"), RiskEntitySource.Target, name, type, tags, [], 0.9, new Dictionary<string, string>());

    private static void AssertAssessment(
        IReadOnlyList<RiskEntityAssessment> results,
        string candidateId,
        RiskDisposition disposition,
        RiskPolicySuggestion policy,
        string ruleName)
    {
        var result = Assert.Single(results, entity => entity.CandidateId == candidateId);
        Assert.Equal(disposition, result.Disposition);
        Assert.Equal(policy, result.SuggestedPolicy);
        Assert.Equal(ruleName, result.MatchedRuleName);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
