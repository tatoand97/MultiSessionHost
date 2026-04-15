namespace MultiSessionHost.Desktop.Extraction;

public sealed class TargetDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public TargetDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var targets = new List<DetectedTarget>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = _classifier.ClassifyTarget(node, _query);

            if (classification.Confidence == DetectionConfidence.Unknown)
            {
                continue;
            }

            var label = SemanticParsing.LabelFor(node, _query);
            var active = SemanticParsing.IsTrue(_query.GetAttribute(node, "active")) ||
                SemanticParsing.ContainsAny(label, "active", "current");
            var focused = SemanticParsing.IsTrue(_query.GetAttribute(node, "focused"));

            targets.Add(new DetectedTarget(
                node.Id.Value,
                label,
                node.Selected,
                active,
                focused,
                SemanticParsing.ParseInt(_query.GetAttribute(node, "count")),
                SemanticParsing.ParseInt(_query.GetAttribute(node, "index")) ?? SemanticParsing.ParseInt(_query.GetAttribute(node, "selectedIndex")),
                classification.Kind,
                classification.Confidence));
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with { Targets = targets });
    }
}
