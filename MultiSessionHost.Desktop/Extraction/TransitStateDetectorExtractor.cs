namespace MultiSessionHost.Desktop.Extraction;

public sealed class TransitStateDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public TransitStateDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var transitStates = new List<DetectedTransitState>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = _classifier.ClassifyTransit(node, _query);

            if (classification.Confidence == DetectionConfidence.Unknown)
            {
                continue;
            }

            var label = SemanticParsing.LabelFor(node, _query);
            var reasons = new[]
            {
                classification.Rationale,
                _query.GetAttribute(node, "status"),
                _query.GetAttribute(node, "progressText")
            }.Where(static reason => !string.IsNullOrWhiteSpace(reason)).Select(static reason => reason!).ToArray();

            transitStates.Add(new DetectedTransitState(
                classification.Kind,
                [node.Id.Value],
                label,
                SemanticParsing.GetPercent(node, _query),
                reasons,
                classification.Confidence));
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with { TransitStates = transitStates });
    }
}
