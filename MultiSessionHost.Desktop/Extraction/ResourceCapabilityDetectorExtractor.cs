namespace MultiSessionHost.Desktop.Extraction;

public sealed class ResourceCapabilityDetectorExtractor : IUiSemanticExtractor
{
    private readonly IUiTreeQueryService _query;
    private readonly IUiSemanticClassifier _classifier;

    public ResourceCapabilityDetectorExtractor(IUiTreeQueryService query, IUiSemanticClassifier classifier)
    {
        _query = query;
        _classifier = classifier;
    }

    public ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken)
    {
        var resources = new List<DetectedResource>();
        var capabilities = new List<DetectedCapability>();
        var warnings = new List<string>();

        foreach (var node in _query.Flatten(context.UiTree).Where(static node => node.Visible))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resourceClassification = _classifier.ClassifyResource(node, _query);
            var capabilityClassification = _classifier.ClassifyCapability(node, _query);

            if (resourceClassification.Confidence != DetectionConfidence.Unknown)
            {
                var percent = SemanticParsing.GetPercent(node, _query);
                var degraded = SemanticParsing.IsTrue(_query.GetAttribute(node, "degraded")) || percent is < 35;
                var critical = SemanticParsing.IsTrue(_query.GetAttribute(node, "critical")) || percent is < 15;

                if (resourceClassification.Confidence == DetectionConfidence.Low && percent is null)
                {
                    warnings.Add($"Node '{node.Id.Value}' looked resource-like but exposed no numeric value.");
                }

                resources.Add(new DetectedResource(
                    node.Id.Value,
                    SemanticParsing.LabelFor(node, _query),
                    resourceClassification.Kind,
                    percent,
                    SemanticParsing.ParseDouble(_query.GetAttribute(node, "value")),
                    degraded,
                    critical,
                    resourceClassification.Confidence));
            }

            if (capabilityClassification.Confidence != DetectionConfidence.Unknown)
            {
                var label = SemanticParsing.LabelFor(node, _query);
                var status = capabilityClassification.Kind;
                var active = status == CapabilityStatus.Active || SemanticParsing.IsTrue(_query.GetAttribute(node, "active"));
                var coolingDown = status == CapabilityStatus.CoolingDown || SemanticParsing.IsTrue(_query.GetAttribute(node, "coolingDown"));

                capabilities.Add(new DetectedCapability(
                    node.Id.Value,
                    label,
                    status,
                    node.Enabled && status != CapabilityStatus.Disabled,
                    active,
                    coolingDown,
                    capabilityClassification.Confidence));
            }
        }

        return ValueTask.FromResult(UiSemanticExtractionContribution.Empty with
        {
            Resources = resources,
            Capabilities = capabilities,
            Warnings = warnings
        });
    }
}
