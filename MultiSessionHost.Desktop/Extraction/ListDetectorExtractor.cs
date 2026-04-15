namespace MultiSessionHost.Desktop.Extraction;

public sealed class ListDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public ListDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var lists = new List<DetectedList>();
        var warnings = new List<string>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var classification = _classifier.ClassifyList(node, _query);

            if (classification.Confidence == DetectionConfidence.Unknown)
            {
                continue;
            }

            var itemLabels = SemanticParsing.GetJsonStringArrayAttribute(node, _query, "items");
            var itemCount = SemanticParsing.ParseInt(_query.GetAttribute(node, "itemCount")) ?? itemLabels.Count;
            var selectedItem = _query.GetAttribute(node, "selectedItem");
            var selectedCount = SemanticParsing.ParseInt(_query.GetAttribute(node, "selectedItemCount")) ??
                (string.IsNullOrWhiteSpace(selectedItem) ? 0 : 1);
            var isScrollable = SemanticParsing.IsTrue(_query.GetAttribute(node, "scrollable")) ||
                SemanticParsing.IsTrue(_query.GetAttribute(node, "canScroll"));

            if (classification.Confidence == DetectionConfidence.Low && itemCount == 0)
            {
                warnings.Add($"Node '{node.Id.Value}' looked list-like but exposed no item count.");
            }

            lists.Add(new DetectedList(
                node.Id.Value,
                SemanticParsing.LabelFor(node, _query),
                itemCount,
                selectedCount,
                itemLabels.Take(25).ToArray(),
                isScrollable,
                classification.Kind,
                classification.Confidence));
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with { Lists = lists, Warnings = warnings });
    }
}
