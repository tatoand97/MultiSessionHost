namespace MultiSessionHost.Desktop.Extraction;

public sealed class PresenceEntityDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public PresenceEntityDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var entities = new List<DetectedPresenceEntity>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = _classifier.ClassifyPresenceEntity(node, _query);

            if (classification.Confidence == DetectionConfidence.Unknown)
            {
                continue;
            }

            var membership = SemanticParsing.GetJsonStringArrayAttribute(node, _query, "items");
            var count = SemanticParsing.ParseInt(_query.GetAttribute(node, "entityCount")) ??
                SemanticParsing.ParseInt(_query.GetAttribute(node, "itemCount")) ??
                (membership.Count == 0 ? null : membership.Count);

            entities.Add(new DetectedPresenceEntity(
                node.Id.Value,
                SemanticParsing.LabelFor(node, _query),
                count,
                membership.Take(25).ToArray(),
                classification.Kind,
                _query.GetAttribute(node, "status"),
                classification.Confidence));
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with { PresenceEntities = entities });
    }
}
