namespace MultiSessionHost.Contracts.Sessions;

public sealed record RiskAssessmentResultDto(
    string SessionId,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<RiskEntityAssessmentDto> Entities,
    RiskAssessmentSummaryDto Summary,
    IReadOnlyList<string> Warnings);

public sealed record RiskAssessmentSummaryDto(
    int SafeCount,
    int UnknownCount,
    int ThreatCount,
    string HighestSeverity,
    int HighestPriority,
    bool HasWithdrawPolicy,
    string? TopCandidateId,
    string? TopCandidateName,
    string? TopCandidateType,
    string TopSuggestedPolicy);

public sealed record RiskEntityAssessmentDto(
    string CandidateId,
    string Source,
    string Name,
    string Type,
    IReadOnlyList<string> Tags,
    string Disposition,
    string Severity,
    int Priority,
    string SuggestedPolicy,
    string? MatchedRuleName,
    IReadOnlyList<string> Reasons,
    double Confidence,
    IReadOnlyDictionary<string, string> Metadata);
