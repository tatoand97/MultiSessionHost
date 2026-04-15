namespace MultiSessionHost.Desktop.Risk;

public interface IRiskRuleProvider
{
    IReadOnlyList<RiskRule> GetActiveRules();
}
