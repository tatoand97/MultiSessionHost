using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;

namespace MultiSessionHost.Desktop.Risk;

public sealed class DefaultRiskClassificationPipeline : IRiskClassificationPipeline
{
    private readonly RiskClassificationOptions _options;
    private readonly ISessionSemanticExtractionStore _semanticExtractionStore;
    private readonly IRiskCandidateBuilder _candidateBuilder;
    private readonly IRiskRuleProvider _ruleProvider;
    private readonly IRiskClassifier _classifier;
    private readonly ISessionRiskAssessmentStore _assessmentStore;
    private readonly IClock _clock;

    public DefaultRiskClassificationPipeline(
        SessionHostOptions options,
        ISessionSemanticExtractionStore semanticExtractionStore,
        IRiskCandidateBuilder candidateBuilder,
        IRiskRuleProvider ruleProvider,
        IRiskClassifier classifier,
        ISessionRiskAssessmentStore assessmentStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.RiskClassification;
        _semanticExtractionStore = semanticExtractionStore;
        _candidateBuilder = candidateBuilder;
        _ruleProvider = ruleProvider;
        _classifier = classifier;
        _assessmentStore = assessmentStore;
        _clock = clock;
    }

    public async ValueTask<RiskAssessmentResult> AssessAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (!_options.EnableRiskClassification)
        {
            var disabled = RiskAssessmentResult.Empty(sessionId, now) with
            {
                Warnings = ["Risk classification is disabled."]
            };
            return await _assessmentStore.UpdateAsync(sessionId, disabled, cancellationToken).ConfigureAwait(false);
        }

        var semanticExtraction = await _semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (semanticExtraction is null)
        {
            var missing = RiskAssessmentResult.Empty(sessionId, now) with
            {
                Warnings = ["No semantic extraction result was available for risk classification."]
            };
            return await _assessmentStore.UpdateAsync(sessionId, missing, cancellationToken).ConfigureAwait(false);
        }

        var candidates = _candidateBuilder.BuildCandidates(semanticExtraction);
        var entities = _classifier.Classify(candidates, _ruleProvider.GetActiveRules());
        var result = new RiskAssessmentResult(
            sessionId,
            now,
            entities,
            BuildSummary(entities),
            semanticExtraction.Warnings
                .Select(static warning => $"Semantic warning: {warning}")
                .ToArray());

        return await _assessmentStore.UpdateAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
    }

    private static RiskAssessmentSummary BuildSummary(IReadOnlyList<RiskEntityAssessment> entities)
    {
        var top = entities
            .OrderByDescending(static entity => entity.Priority)
            .ThenByDescending(static entity => entity.Severity)
            .ThenByDescending(static entity => entity.Confidence)
            .FirstOrDefault();

        return new RiskAssessmentSummary(
            SafeCount: entities.Count(static entity => entity.Disposition == RiskDisposition.Safe),
            UnknownCount: entities.Count(static entity => entity.Disposition == RiskDisposition.Unknown),
            ThreatCount: entities.Count(static entity => entity.Disposition == RiskDisposition.Threat),
            HighestSeverity: entities.Count == 0 ? RiskSeverity.Unknown : entities.Max(static entity => entity.Severity),
            HighestPriority: entities.Count == 0 ? 0 : entities.Max(static entity => entity.Priority),
            HasWithdrawPolicy: entities.Any(static entity => entity.SuggestedPolicy is RiskPolicySuggestion.Withdraw or RiskPolicySuggestion.PauseActivity),
            TopCandidateId: top?.CandidateId,
            TopCandidateName: top?.Name,
            TopCandidateType: top?.Type,
            TopSuggestedPolicy: top?.SuggestedPolicy ?? RiskPolicySuggestion.None);
    }
}
