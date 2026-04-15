using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public interface IUiSemanticClassifier
{
    UiSemanticClassification<ListKind> ClassifyList(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<TargetKind> ClassifyTarget(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<AlertSeverity> ClassifyAlert(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<TransitStatus> ClassifyTransit(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<ResourceKind> ClassifyResource(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<CapabilityStatus> ClassifyCapability(UiNode node, IUiTreeQueryService query);

    UiSemanticClassification<PresenceEntityKind> ClassifyPresenceEntity(UiNode node, IUiTreeQueryService query);
}
