namespace MultiSessionHost.Desktop.Risk;

public interface IRiskClassifier
{
    IReadOnlyList<RiskEntityAssessment> Classify(IReadOnlyList<RiskCandidate> candidates, IReadOnlyList<RiskRule> rules);
}
