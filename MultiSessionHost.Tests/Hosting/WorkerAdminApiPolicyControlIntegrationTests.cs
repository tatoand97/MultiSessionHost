using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Hosting;

public sealed class WorkerAdminApiPolicyControlIntegrationTests
{
    [Fact]
    public async Task PolicyControlEndpoints_PauseResumeAndGateExecution()
    {
        const string sessionId = "api-policy-control";
        var options = CreateOptions(sessionId);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        (await client.PostAsync($"/sessions/{sessionId}/decision-plan/evaluate", content: null)).EnsureSuccessStatusCode();

        var initialPolicy = await client.GetFromJsonAsync<SessionPolicyControlStateDto[]>("/policy");
        Assert.NotNull(initialPolicy);
        Assert.Single(initialPolicy!);
        Assert.False(initialPolicy![0].IsPolicyPaused);

        var pauseResponse = await client.PostAsJsonAsync(
            $"/sessions/{sessionId}/pause-policy",
            new PolicyControlActionRequestDto("policy:test-paused", "paused for test", "tester", new Dictionary<string, string>()));
        var pauseResult = await pauseResponse.Content.ReadFromJsonAsync<PolicyControlActionResultDto>();

        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        Assert.NotNull(pauseResult);
        Assert.True(pauseResult!.WasChanged);
        Assert.True(pauseResult.State.IsPolicyPaused);

        var pausedState = await client.GetFromJsonAsync<SessionPolicyControlStateDto>($"/sessions/{sessionId}/policy-state");
        var history = await client.GetFromJsonAsync<SessionPolicyControlHistoryEntryDto[]>($"/sessions/{sessionId}/policy-state/history");
        var pausedPolicy = await client.GetFromJsonAsync<SessionPolicyControlStateDto[]>("/policy");

        Assert.NotNull(pausedState);
        Assert.True(pausedState!.IsPolicyPaused);
        Assert.NotNull(history);
        Assert.Single(history!);
        Assert.NotNull(pausedPolicy);
        Assert.True(pausedPolicy![0].IsPolicyPaused);

        var conflictResponse = await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        var resumeResponse = await client.PostAsJsonAsync(
            $"/sessions/{sessionId}/resume-policy",
            new PolicyControlActionRequestDto("policy:test-resumed", "resumed for test", "tester", new Dictionary<string, string>()));
        var resumeResult = await resumeResponse.Content.ReadFromJsonAsync<PolicyControlActionResultDto>();

        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        Assert.NotNull(resumeResult);
        Assert.False(resumeResult!.State.IsPolicyPaused);

        var executeResponse = await client.PostAsync($"/sessions/{sessionId}/decision-plan/execute", content: null);
        var execution = await executeResponse.Content.ReadFromJsonAsync<DecisionPlanExecutionDto>();

        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        Assert.NotNull(execution);
        Assert.False(execution!.WasAutoExecuted);
    }

    [Fact]
    public async Task PolicyControlEndpoints_ReturnNotFoundForUnknownSession()
    {
        const string sessionId = "api-policy-known";
        var options = CreateOptions(sessionId);

        await using var harness = await WorkerHostHarness.StartAsync(options);
        var client = Assert.IsType<HttpClient>(harness.Client);

        var response = await client.GetAsync("/sessions/api-policy-missing/policy-state");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static SessionHostOptions CreateOptions(string sessionId) =>
        new()
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = true,
            AdminApiUrl = "http://127.0.0.1:0",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            DecisionExecution = new DecisionExecutionOptions
            {
                EnableDecisionExecution = true,
                AutoExecuteAfterEvaluation = false,
                MaxHistoryEntries = 20,
                RepeatSuppressionWindowMs = 0,
                FailOnUnhandledBlockingDirective = false,
                RecordNoOpExecutions = true
            },
            Sessions = [TestOptionsFactory.Session(sessionId, enabled: false)]
        };
}