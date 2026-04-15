using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;

namespace MultiSessionHost.AdminDesktop.Api;

public sealed class AdminApiClient : IAdminApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public AdminApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public Uri? BaseAddress => httpClient.BaseAddress;

    public void ConfigureBaseAddress(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseAddress))
        {
            throw new AdminApiException(null, $"Invalid base URL '{baseUrl}'.");
        }

        httpClient.BaseAddress = baseAddress;
    }

    public Task<ProcessHealthDto> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<ProcessHealthDto>("/health", cancellationToken);

    public Task<IReadOnlyList<SessionInfoDto>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        GetJsonListAsync<SessionInfoDto>("/sessions", cancellationToken);

    public Task<SessionInfoDto?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionInfoDto>($"/sessions/{Escape(sessionId)}", cancellationToken);

    public Task<SessionTargetDto?> GetTargetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionTargetDto>($"/sessions/{Escape(sessionId)}/target", cancellationToken);

    public Task<IReadOnlyList<DesktopTargetProfileDto>> GetTargetsAsync(CancellationToken cancellationToken = default) =>
        GetJsonListAsync<DesktopTargetProfileDto>("/targets", cancellationToken);

    public Task<DesktopTargetProfileDto?> GetTargetProfileAsync(string profileName, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DesktopTargetProfileDto>($"/targets/{Escape(profileName)}", cancellationToken);

    public Task<BindingStoreSnapshotDto> GetBindingsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<BindingStoreSnapshotDto>("/bindings", cancellationToken);

    public Task<SessionTargetBindingDto?> GetBindingAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionTargetBindingDto>($"/bindings/{Escape(sessionId)}", cancellationToken);

    public Task<SessionTargetBindingDto> SaveBindingAsync(string sessionId, SessionTargetBindingUpsertRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<SessionTargetBindingUpsertRequest, SessionTargetBindingDto>(HttpMethod.Put, $"/bindings/{Escape(sessionId)}", request, cancellationToken);

    public async Task DeleteBindingAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await SendRawAsync<object?>(HttpMethod.Delete, $"/bindings/{Escape(sessionId)}", requestBody: null, cancellationToken).ConfigureAwait(false);
    }

    public Task<SessionUiDto?> GetSessionUiAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionUiDto>($"/sessions/{Escape(sessionId)}/ui", cancellationToken);

    public Task<SessionUiRawDto?> GetSessionUiRawAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionUiRawDto>($"/sessions/{Escape(sessionId)}/ui/raw", cancellationToken);

    public Task<SessionUiRefreshDto> RefreshSessionUiAsync(string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<SessionUiRefreshDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/ui/refresh", content: null, cancellationToken);

    public Task<UiSemanticExtractionResultDto?> GetSemanticAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<UiSemanticExtractionResultDto>($"/sessions/{Escape(sessionId)}/semantic", cancellationToken);

    public Task<SemanticSummaryDto?> GetSemanticSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SemanticSummaryDto>($"/sessions/{Escape(sessionId)}/semantic/summary", cancellationToken);

    public Task<RiskAssessmentResultDto?> GetRiskAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<RiskAssessmentResultDto>($"/sessions/{Escape(sessionId)}/risk", cancellationToken);

    public Task<RiskAssessmentSummaryDto?> GetRiskSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<RiskAssessmentSummaryDto>($"/sessions/{Escape(sessionId)}/risk/summary", cancellationToken);

    public Task<IReadOnlyList<RiskEntityAssessmentDto>> GetRiskEntitiesAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonListAsync<RiskEntityAssessmentDto>($"/sessions/{Escape(sessionId)}/risk/entities", cancellationToken);

    public Task<IReadOnlyList<RiskEntityAssessmentDto>> GetRiskThreatsAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonListAsync<RiskEntityAssessmentDto>($"/sessions/{Escape(sessionId)}/risk/threats", cancellationToken);

    public Task<SessionDomainStateDto?> GetDomainAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionDomainStateDto>($"/sessions/{Escape(sessionId)}/domain", cancellationToken);

    public Task<SessionActivitySnapshotDto?> GetActivityAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionActivitySnapshotDto>($"/sessions/{Escape(sessionId)}/activity", cancellationToken);

    public Task<SessionActivityHistoryDto?> GetActivityHistoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionActivityHistoryDto>($"/sessions/{Escape(sessionId)}/activity/history", cancellationToken);

    public Task<DecisionPlanDto?> GetDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanDto>($"/sessions/{Escape(sessionId)}/decision-plan", cancellationToken);

    public Task<DecisionPlanExplanationDto?> GetDecisionPlanExplanationAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanExplanationDto>($"/sessions/{Escape(sessionId)}/decision-plan/explanation", cancellationToken);

    public Task<DecisionPlanSummaryDto?> GetDecisionPlanSummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanSummaryDto>($"/sessions/{Escape(sessionId)}/decision-plan/summary", cancellationToken);

    public Task<DecisionPlanHistoryDto?> GetDecisionPlanHistoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanHistoryDto>($"/sessions/{Escape(sessionId)}/decision-plan/history", cancellationToken);

    public Task<DecisionPlanDto> EvaluateDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default) =>
        SendJsonAsync<object?, DecisionPlanDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/decision-plan/evaluate", null, cancellationToken);

    public Task<DecisionPlanExecutionDto?> GetDecisionExecutionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanExecutionDto>($"/sessions/{Escape(sessionId)}/decision-execution", cancellationToken);

    public Task<DecisionPlanExecutionHistoryDto?> GetDecisionExecutionHistoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<DecisionPlanExecutionHistoryDto>($"/sessions/{Escape(sessionId)}/decision-execution/history", cancellationToken);

    public Task<DecisionPlanExecutionDto> ExecuteDecisionPlanAsync(string sessionId, CancellationToken cancellationToken = default) =>
        SendJsonAsync<object?, DecisionPlanExecutionDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/decision-plan/execute", null, cancellationToken);

    public Task<SessionOperationalMemorySnapshotDto?> GetMemoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionOperationalMemorySnapshotDto>($"/sessions/{Escape(sessionId)}/memory", cancellationToken);

    public Task<SessionOperationalMemorySummaryDto?> GetMemorySummaryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionOperationalMemorySummaryDto>($"/sessions/{Escape(sessionId)}/memory/summary", cancellationToken);

    public Task<PolicyMemoryContextDto?> GetMemoryContextAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<PolicyMemoryContextDto>($"/sessions/{Escape(sessionId)}/memory/context", cancellationToken);

    public Task<SessionOperationalMemoryHistoryDto?> GetMemoryHistoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionOperationalMemoryHistoryDto>($"/sessions/{Escape(sessionId)}/memory/history", cancellationToken);

    public Task<RuntimePersistenceStatusDto> GetPersistenceAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<RuntimePersistenceStatusDto>("/persistence", cancellationToken);

    public Task<RuntimePersistenceSessionStatusDto?> GetSessionPersistenceAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<RuntimePersistenceSessionStatusDto>($"/sessions/{Escape(sessionId)}/persistence", cancellationToken);

    public Task<RuntimePersistenceStatusDto> FlushPersistenceAsync(CancellationToken cancellationToken = default) =>
        SendAsync<RuntimePersistenceStatusDto>(HttpMethod.Post, "/persistence/flush", content: null, cancellationToken);

    public Task<RuntimePersistenceSessionStatusDto> FlushSessionPersistenceAsync(string sessionId, CancellationToken cancellationToken = default) =>
        SendAsync<RuntimePersistenceSessionStatusDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/persistence/flush", content: null, cancellationToken);

    public Task<ExecutionCoordinationSnapshotDto> GetCoordinationAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<ExecutionCoordinationSnapshotDto>("/coordination", cancellationToken);

    public Task<ExecutionCoordinationSnapshotDto?> GetCoordinationForSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<ExecutionCoordinationSnapshotDto>($"/coordination/sessions/{Escape(sessionId)}", cancellationToken);

    public Task<PolicyRuleSetDto> GetPolicyRulesAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<PolicyRuleSetDto>("/policy-rules", cancellationToken);

    public async Task<IReadOnlyList<PolicyRuleDto>> GetPolicyRuleFamilyAsync(string family, CancellationToken cancellationToken = default) =>
        await GetJsonListAsync<PolicyRuleDto>($"/policy-rules/{Escape(family)}", cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<SessionPolicyControlStateDto>> GetPolicyStatesAsync(CancellationToken cancellationToken = default) =>
        GetJsonListAsync<SessionPolicyControlStateDto>("/policy", cancellationToken);

    public Task<SessionPolicyControlStateDto?> GetSessionPolicyStateAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonOrNullAsync<SessionPolicyControlStateDto>($"/sessions/{Escape(sessionId)}/policy-state", cancellationToken);

    public Task<IReadOnlyList<SessionPolicyControlHistoryEntryDto>> GetSessionPolicyHistoryAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetJsonListAsync<SessionPolicyControlHistoryEntryDto>($"/sessions/{Escape(sessionId)}/policy-state/history", cancellationToken);

    public Task<PolicyControlActionResultDto> PausePolicyAsync(string sessionId, PolicyControlActionRequestDto? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<PolicyControlActionRequestDto?, PolicyControlActionResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/pause-policy", request, cancellationToken);

    public Task<PolicyControlActionResultDto> ResumePolicyAsync(string sessionId, PolicyControlActionRequestDto? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<PolicyControlActionRequestDto?, PolicyControlActionResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/resume-policy", request, cancellationToken);

    public Task<UiCommandResultDto> SendCommandAsync(string sessionId, UiCommandRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<UiCommandRequest, UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/commands", request, cancellationToken);

    public Task<UiCommandResultDto> ClickNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken = default) =>
        SendAsync<UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/nodes/{Escape(nodeId)}/click", content: null, cancellationToken);

    public Task<UiCommandResultDto> InvokeNodeAsync(string sessionId, string nodeId, NodeInvokeCommandRequest? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<NodeInvokeCommandRequest?, UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/nodes/{Escape(nodeId)}/invoke", request, cancellationToken);

    public Task<UiCommandResultDto> SetNodeTextAsync(string sessionId, string nodeId, NodeTextCommandRequest? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<NodeTextCommandRequest?, UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/nodes/{Escape(nodeId)}/text", request, cancellationToken);

    public Task<UiCommandResultDto> ToggleNodeAsync(string sessionId, string nodeId, NodeToggleCommandRequest? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<NodeToggleCommandRequest?, UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/nodes/{Escape(nodeId)}/toggle", request, cancellationToken);

    public Task<UiCommandResultDto> SelectNodeAsync(string sessionId, string nodeId, NodeSelectCommandRequest? request = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<NodeSelectCommandRequest?, UiCommandResultDto>(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/nodes/{Escape(nodeId)}/select", request, cancellationToken);

    public async Task<HttpResponseMessage> StartSessionAsync(string sessionId, StartSessionRequest? request = null, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/start", request, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> StopSessionAsync(string sessionId, StopSessionRequest? request = null, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/stop", request, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> PauseSessionAsync(string sessionId, PauseSessionRequest? request = null, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/pause", request, cancellationToken).ConfigureAwait(false);

    public async Task<HttpResponseMessage> ResumeSessionAsync(string sessionId, ResumeSessionRequest? request = null, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Post, $"/sessions/{Escape(sessionId)}/resume", request, cancellationToken).ConfigureAwait(false);

    public Task<SessionStateDto?> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken = default) =>
        GetSessionAsync(sessionId, cancellationToken).ContinueWith(task => task.Result?.State, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> GetJsonListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T[]>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetJsonOrNullAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<TRequest, T>(HttpMethod method, string path, TRequest? request, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, request, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRawAsync<TRequest>(HttpMethod method, string path, TRequest? requestBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (requestBody is not null)
        {
            request.Content = JsonContent.Create(requestBody, options: SerializerOptions);
        }

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await AdminApiException.FromResponseAsync(response).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await AdminApiException.FromResponseAsync(response).ConfigureAwait(false);
        }

        if (typeof(T) == typeof(object))
        {
            return (T)(object)new object();
        }

        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        return value is null
            ? throw new AdminApiException(response.StatusCode, $"The response body for '{response.RequestMessage?.RequestUri}' was empty.")
            : value;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
