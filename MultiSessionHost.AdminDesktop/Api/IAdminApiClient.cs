using System.Net.Http;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;

namespace MultiSessionHost.AdminDesktop.Api;

public interface IAdminApiClient
{
    Uri? BaseAddress { get; }

    void ConfigureBaseAddress(string baseUrl);

    Task<ProcessHealthDto> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionInfoDto>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<SessionInfoDto?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionTargetDto?> GetTargetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesktopTargetProfileDto>> GetTargetsAsync(CancellationToken cancellationToken = default);

    Task<DesktopTargetProfileDto?> GetTargetProfileAsync(string profileName, CancellationToken cancellationToken = default);

    Task<BindingStoreSnapshotDto> GetBindingsAsync(CancellationToken cancellationToken = default);

    Task<SessionTargetBindingDto?> GetBindingAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionTargetBindingDto> SaveBindingAsync(string sessionId, SessionTargetBindingUpsertRequest request, CancellationToken cancellationToken = default);

    Task DeleteBindingAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionUiDto?> GetSessionUiAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionUiRawDto?> GetSessionUiRawAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<GlobalObservabilitySnapshotDto> GetObservabilityAsync(CancellationToken cancellationToken = default);

    Task<SessionObservabilityDto?> GetSessionObservabilityAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionObservabilityEventDto>> GetSessionObservabilityEventsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionObservabilityMetricsDto?> GetSessionObservabilityMetricsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdapterErrorRecordDto>> GetSessionObservabilityErrorsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionUiRefreshDto> RefreshSessionUiAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<UiSemanticExtractionResultDto?> GetSemanticAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SemanticSummaryDto?> GetSemanticSummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RiskAssessmentResultDto?> GetRiskAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RiskAssessmentSummaryDto?> GetRiskSummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskEntityAssessmentDto>> GetRiskEntitiesAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskEntityAssessmentDto>> GetRiskThreatsAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionDomainStateDto?> GetDomainAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionActivitySnapshotDto?> GetActivityAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionActivityHistoryDto?> GetActivityHistoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanDto?> GetDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanExplanationDto?> GetDecisionPlanExplanationAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanSummaryDto?> GetDecisionPlanSummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanHistoryDto?> GetDecisionPlanHistoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanDto> EvaluateDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanExecutionDto?> GetDecisionExecutionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanExecutionHistoryDto?> GetDecisionExecutionHistoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionPlanExecutionDto> ExecuteDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionOperationalMemorySnapshotDto?> GetMemoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionOperationalMemorySummaryDto?> GetMemorySummaryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<PolicyMemoryContextDto?> GetMemoryContextAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<SessionOperationalMemoryHistoryDto?> GetMemoryHistoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RuntimePersistenceStatusDto> GetPersistenceAsync(CancellationToken cancellationToken = default);

    Task<RuntimePersistenceSessionStatusDto?> GetSessionPersistenceAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RuntimePersistenceStatusDto> FlushPersistenceAsync(CancellationToken cancellationToken = default);

    Task<RuntimePersistenceSessionStatusDto> FlushSessionPersistenceAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ExecutionCoordinationSnapshotDto> GetCoordinationAsync(CancellationToken cancellationToken = default);

    Task<ExecutionCoordinationSnapshotDto?> GetCoordinationForSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<PolicyRuleSetDto> GetPolicyRulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolicyRuleDto>> GetPolicyRuleFamilyAsync(string family, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionPolicyControlStateDto>> GetPolicyStatesAsync(CancellationToken cancellationToken = default);

    Task<SessionPolicyControlStateDto?> GetSessionPolicyStateAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionPolicyControlHistoryEntryDto>> GetSessionPolicyHistoryAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<PolicyControlActionResultDto> PausePolicyAsync(string sessionId, PolicyControlActionRequestDto? request = null, CancellationToken cancellationToken = default);

    Task<PolicyControlActionResultDto> ResumePolicyAsync(string sessionId, PolicyControlActionRequestDto? request = null, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> SendCommandAsync(string sessionId, UiCommandRequest request, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> ClickNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> InvokeNodeAsync(string sessionId, string nodeId, NodeInvokeCommandRequest? request = null, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> SetNodeTextAsync(string sessionId, string nodeId, NodeTextCommandRequest? request = null, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> ToggleNodeAsync(string sessionId, string nodeId, NodeToggleCommandRequest? request = null, CancellationToken cancellationToken = default);

    Task<UiCommandResultDto> SelectNodeAsync(string sessionId, string nodeId, NodeSelectCommandRequest? request = null, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> StartSessionAsync(string sessionId, StartSessionRequest? request = null, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> StopSessionAsync(string sessionId, StopSessionRequest? request = null, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> PauseSessionAsync(string sessionId, PauseSessionRequest? request = null, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> ResumeSessionAsync(string sessionId, ResumeSessionRequest? request = null, CancellationToken cancellationToken = default);

    Task<SessionStateDto?> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default);
}
