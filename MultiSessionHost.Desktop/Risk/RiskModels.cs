using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Risk;

public sealed record RiskCandidate(
    string CandidateId,
    SessionId SessionId,
    RiskEntitySource Source,
    string Name,
    string Type,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Signals,
    double Confidence,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RiskRule(
    string RuleName,
    IReadOnlyList<string> MatchByName,
    RiskRuleMatchMode NameMatchMode,
    IReadOnlyList<string> MatchByType,
    RiskRuleMatchMode TypeMatchMode,
    IReadOnlyList<string> MatchByTags,
    bool RequireAllTags,
    RiskDisposition Disposition,
    RiskSeverity Severity,
    int Priority,
    RiskPolicySuggestion SuggestedPolicy,
    string Reason);

public sealed record RiskRuleMatch(
    string RuleName,
    IReadOnlyList<string> MatchedCriteria,
    string Reason);

public sealed record RiskEntityAssessment(
    string CandidateId,
    RiskEntitySource Source,
    string Name,
    string Type,
    IReadOnlyList<string> Tags,
    RiskDisposition Disposition,
    RiskSeverity Severity,
    int Priority,
    RiskPolicySuggestion SuggestedPolicy,
    string? MatchedRuleName,
    IReadOnlyList<string> Reasons,
    double Confidence,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RiskAssessmentSummary(
    int SafeCount,
    int UnknownCount,
    int ThreatCount,
    RiskSeverity HighestSeverity,
    int HighestPriority,
    bool HasWithdrawPolicy,
    string? TopCandidateId,
    string? TopCandidateName,
    string? TopCandidateType,
    RiskPolicySuggestion TopSuggestedPolicy);

public sealed record RiskAssessmentResult(
    SessionId SessionId,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<RiskEntityAssessment> Entities,
    RiskAssessmentSummary Summary,
    IReadOnlyList<string> Warnings)
{
    public static RiskAssessmentResult Empty(SessionId sessionId, DateTimeOffset now) =>
        new(
            sessionId,
            now,
            [],
            new RiskAssessmentSummary(
                SafeCount: 0,
                UnknownCount: 0,
                ThreatCount: 0,
                HighestSeverity: RiskSeverity.Unknown,
                HighestPriority: 0,
                HasWithdrawPolicy: false,
                TopCandidateId: null,
                TopCandidateName: null,
                TopCandidateType: null,
                TopSuggestedPolicy: RiskPolicySuggestion.None),
            []);
}
