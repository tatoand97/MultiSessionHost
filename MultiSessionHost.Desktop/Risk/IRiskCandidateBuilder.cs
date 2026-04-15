using MultiSessionHost.Desktop.Extraction;

namespace MultiSessionHost.Desktop.Risk;

public interface IRiskCandidateBuilder
{
    IReadOnlyList<RiskCandidate> BuildCandidates(UiSemanticExtractionResult semanticExtraction);
}
