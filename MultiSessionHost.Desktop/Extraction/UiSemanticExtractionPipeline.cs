namespace MultiSessionHost.Desktop.Extraction;

public sealed class UiSemanticExtractionPipeline : IUiSemanticExtractionPipeline
{
    private readonly IReadOnlyList<IUiSemanticExtractor> _extractors;

    public UiSemanticExtractionPipeline(IEnumerable<IUiSemanticExtractor> extractors)
    {
        _extractors = extractors.ToArray();
    }

    public async ValueTask<UiSemanticExtractionResult> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var lists = new List<DetectedList>();
        var targets = new List<DetectedTarget>();
        var alerts = new List<DetectedAlert>();
        var transitStates = new List<DetectedTransitState>();
        var resources = new List<DetectedResource>();
        var capabilities = new List<DetectedCapability>();
        var presenceEntities = new List<DetectedPresenceEntity>();
        var warnings = new List<string>();

        foreach (var extractor in _extractors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contribution = await extractor.ExtractAsync(context, cancellationToken).ConfigureAwait(false);
            lists.AddRange(contribution.Lists);
            targets.AddRange(contribution.Targets);
            alerts.AddRange(contribution.Alerts);
            transitStates.AddRange(contribution.TransitStates);
            resources.AddRange(contribution.Resources);
            capabilities.AddRange(contribution.Capabilities);
            presenceEntities.AddRange(contribution.PresenceEntities);
            warnings.AddRange(contribution.Warnings);
        }

        var confidenceSummary = new Dictionary<string, DetectionConfidence>(StringComparer.Ordinal)
        {
            ["lists"] = SummarizeConfidence(lists.Select(static item => item.Confidence)),
            ["targets"] = SummarizeConfidence(targets.Select(static item => item.Confidence)),
            ["alerts"] = SummarizeConfidence(alerts.Select(static item => item.Confidence)),
            ["transitStates"] = SummarizeConfidence(transitStates.Select(static item => item.Confidence)),
            ["resources"] = SummarizeConfidence(resources.Select(static item => item.Confidence)),
            ["capabilities"] = SummarizeConfidence(capabilities.Select(static item => item.Confidence)),
            ["presenceEntities"] = SummarizeConfidence(presenceEntities.Select(static item => item.Confidence))
        };

        return new UiSemanticExtractionResult(
            context.SessionId,
            context.Now,
            lists.DistinctBy(static item => item.NodeId).ToArray(),
            targets.DistinctBy(static item => item.NodeId).ToArray(),
            alerts.DistinctBy(static item => item.NodeId).ToArray(),
            transitStates.DistinctBy(static item => string.Join('|', item.NodeIds)).ToArray(),
            resources.DistinctBy(static item => item.NodeId).ToArray(),
            capabilities.DistinctBy(static item => item.NodeId).ToArray(),
            presenceEntities.DistinctBy(static item => item.NodeId).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            confidenceSummary);
    }

    private static DetectionConfidence SummarizeConfidence(IEnumerable<DetectionConfidence> values)
    {
        var materialized = values.ToArray();

        if (materialized.Length == 0)
        {
            return DetectionConfidence.Unknown;
        }

        return materialized.Max();
    }
}
