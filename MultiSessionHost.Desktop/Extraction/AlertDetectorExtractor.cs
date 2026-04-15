namespace MultiSessionHost.Desktop.Extraction;

public sealed class AlertDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public AlertDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var alerts = new List<DetectedAlert>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = _classifier.ClassifyAlert(node, _query);

            if (classification.Confidence == DetectionConfidence.Unknown)
            {
                continue;
            }

            var message = string.Join(' ', _query.GatherTextCandidates(node));

            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            alerts.Add(new DetectedAlert(
                node.Id.Value,
                message,
                classification.Kind,
                node.Visible,
                _query.GetAttribute(node, "source") ?? node.Role,
                classification.Confidence));
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with { Alerts = alerts });
    }
}
