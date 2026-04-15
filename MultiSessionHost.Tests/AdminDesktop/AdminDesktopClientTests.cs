using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MultiSessionHost.AdminDesktop.Api;
using MultiSessionHost.AdminDesktop.Services;
using MultiSessionHost.AdminDesktop.ViewModels;
using MultiSessionHost.Contracts.Coordination;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.AdminDesktop;

public sealed class AdminDesktopClientTests
{
    [Fact]
    public async Task SaveBindingAsync_SerializesRequestAndReturnsBinding()
    {
        var captured = new List<CapturedRequest>();
        var client = CreateClient(request =>
        {
            captured.Add(CapturedRequest.From(request));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SessionTargetBindingDto(
                    "alpha",
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7200" },
                    null))
            };

            return response;
        });

        var result = await client.SaveBindingAsync(
            "alpha",
            new SessionTargetBindingUpsertRequest(
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7200" },
                Overrides: null));

        Assert.Equal("alpha", result.SessionId);
        Assert.Single(captured);
        Assert.Equal(HttpMethod.Put, captured[0].Method);
        Assert.Equal("/bindings/alpha", captured[0].Path);
        Assert.Contains("\"targetProfileName\":\"test-app\"", captured[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Port\":\"7200\"", captured[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConflictResponse_IsMappedToTypedException()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"error\":\"Policy-driven execution is paused.\"}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<AdminApiException>(() => client.ExecuteDecisionPlanAsync("alpha"));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("paused", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Policy-driven execution", exception.ResponseText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFoundSession_ReturnsNull()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var session = await client.GetSessionAsync("missing");

        Assert.Null(session);
    }

    private static AdminApiClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new ScriptedHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new AdminApiClient(httpClient);
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Body)
    {
        public static CapturedRequest From(HttpRequestMessage request)
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new CapturedRequest(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body);
        }
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

        public ScriptedHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

public sealed class AdminDesktopViewModelTests
{
    [Fact]
    public async Task ShellSelectionAndOverviewRefresh_LoadsTheSelectedSessionTabs()
    {
        var client = CreateHappyPathClient();
        var refreshCoordinator = new RefreshCoordinator();
        var shell = new ShellViewModel(client, refreshCoordinator)
        {
            BaseUrl = "http://localhost"
        };

        shell.Sessions.Add(new SessionListItemViewModel
        {
            SessionId = "alpha",
            DisplayName = "Alpha Session"
        });

        shell.SelectedSession = shell.Sessions[0];

        Assert.Equal("alpha", shell.SelectedSession?.SessionId);
        Assert.Contains("Selected: alpha", shell.SelectedSessionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OverviewAndBindingActions_InvokeTheApi()
    {
        var requests = new List<CapturedRequest>();
        var client = CreateHappyPathClient(requests);
        var overview = new SessionOverviewViewModel(client);
        await overview.LoadAsync("alpha");

        await overview.PausePolicyCommand.ExecuteAsync(null);
        await overview.ResumePolicyCommand.ExecuteAsync(null);
        await overview.EvaluatePlanCommand.ExecuteAsync(null);
        await overview.ExecutePlanCommand.ExecuteAsync(null);

        Assert.Contains(requests, request => request.Method == HttpMethod.Post && request.Path == "/sessions/alpha/pause-policy");
        Assert.Contains(requests, request => request.Method == HttpMethod.Post && request.Path == "/sessions/alpha/resume-policy");
        Assert.Contains(requests, request => request.Method == HttpMethod.Post && request.Path == "/sessions/alpha/decision-plan/evaluate");
        Assert.Contains(requests, request => request.Method == HttpMethod.Post && request.Path == "/sessions/alpha/decision-plan/execute");

        var binding = new TargetBindingViewModel(client);
        await binding.LoadAsync("alpha");
        binding.TargetProfileName = "updated-profile";
        binding.Variables[0].Value = "7200";
        binding.OverridesJson = JsonSerializer.Serialize(new DesktopTargetProfileOverrideDto(null, null, null, null, null, new Dictionary<string, string?>(), null, null), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await binding.SaveBindingCommand.ExecuteAsync(null);
        await binding.DeleteBindingCommand.ExecuteAsync(null);

        Assert.Contains(requests, request => request.Method == HttpMethod.Put && request.Path == "/bindings/alpha");
        Assert.Contains(requests, request => request.Method == HttpMethod.Delete && request.Path == "/bindings/alpha");
    }

    [Fact]
    public async Task ShellRefresh_HandlesGlobalViewsAndCurrentSession()
    {
        var client = CreateHappyPathClient();
        var refreshCoordinator = new RefreshCoordinator();
        var shell = new ShellViewModel(client, refreshCoordinator)
        {
            BaseUrl = "http://localhost"
        };

        shell.Sessions.Add(new SessionListItemViewModel
        {
            SessionId = "alpha",
            DisplayName = "Alpha Session",
            RuntimeStatus = "Running",
            PolicyPausedText = "Running",
            ActivityState = "Idle"
        });

        shell.Sessions.Add(new SessionListItemViewModel
        {
            SessionId = "beta",
            DisplayName = "Beta Session",
            RuntimeStatus = "Stopped",
            PolicyPausedText = "Paused",
            ActivityState = "Terminal"
        });

        shell.SessionSearchText = "alpha";
        shell.SessionsView.Refresh();

        Assert.Equal(2, shell.Sessions.Count);
        Assert.Single(shell.SessionsView.Cast<object>());
        Assert.NotEmpty(shell.GlobalTabs);
    }

    private static AdminApiClient CreateHappyPathClient(List<CapturedRequest>? capturedRequests = null)
    {
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            capturedRequests?.Add(CapturedRequest.From(request));
            return CreateResponse(request);
        });

        return new AdminApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method;

        if (method == HttpMethod.Get && path == "/sessions")
        {
            return Json(new[] { SampleSessionInfo() });
        }

        if (method == HttpMethod.Get && path == "/policy")
        {
            return Json(new[] { new SessionPolicyControlStateDto("alpha", true, DateTimeOffset.UtcNow.AddMinutes(-5), null, DateTimeOffset.UtcNow.AddMinutes(-4), "paused", "operator", "tester", new Dictionary<string, string>()) });
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha")
        {
            return Json(SampleSessionInfo());
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/activity")
        {
            return Json(new SessionActivitySnapshotDto("alpha", "Idle", "Paused", DateTimeOffset.UtcNow.AddMinutes(-3), "pause", "paused by policy", new Dictionary<string, string>(), new[] { new SessionActivityHistoryEntryDto("Running", "Paused", "pause", "paused", DateTimeOffset.UtcNow.AddMinutes(-3), new Dictionary<string, string>()) }, false));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/decision-plan/summary")
        {
            return Json(new DecisionPlanSummaryDto("alpha", DateTimeOffset.UtcNow.AddMinutes(-2), "Ready", 1, new[] { "policy-a" }, new[] { "policy-a" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/decision-execution")
        {
            return Json(new DecisionPlanExecutionDto("alpha", "fingerprint", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1), null, "Succeeded", false, Array.Empty<DecisionDirectiveExecutionResultDto>(), new DecisionPlanExecutionSummaryDto(1, 1, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), null, null, Array.Empty<string>(), new Dictionary<string, string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/risk/summary")
        {
            return Json(new RiskAssessmentSummaryDto(0, 0, 1, "High", 10, true, "candidate-1", "Threat Node", "Target", "Withdraw"));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/target")
        {
            return Json(new SessionTargetDto(
                "alpha",
                new DesktopTargetProfileDto("test-app", "Desktop", "TestApp.exe", null, null, null, "Exact", new Dictionary<string, string?>(), true, true),
                new SessionTargetBindingDto("alpha", "test-app", new Dictionary<string, string> { ["Port"] = "7100" }, null),
                new ResolvedDesktopTargetDto("alpha", "test-app", "Desktop", "Exact", "TestApp.exe", null, null, null, new Dictionary<string, string?>()),
                new SessionTargetAttachmentDto(1234, "TestApp.exe", null, 1, 2, "Test Window", null, DateTimeOffset.UtcNow),
                "Desktop",
                "TestAdapter"));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/ui")
        {
            return Json(new SessionUiDto("alpha", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, new UiTree(new UiSnapshotMetadata("alpha", "test", DateTimeOffset.UtcNow, 1234, 1, "Test Window", new Dictionary<string, string?>()), new UiNode(new UiNodeId("root"), "Window", "Root", null, null, true, true, false, Array.Empty<UiAttribute>(), new[] { new UiNode(new UiNodeId("child"), "Button", "Action", "Go", null, true, true, false, Array.Empty<UiAttribute>(), Array.Empty<UiNode>()) })), null, new[] { new PlannedUiWorkItem("Click", "Click action", "child") }));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/ui/raw")
        {
            var document = JsonDocument.Parse("{\"root\":true}");
            return Json(new SessionUiRawDto("alpha", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, document.RootElement.Clone()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/semantic")
        {
            return Json(new UiSemanticExtractionResultDto("alpha", DateTimeOffset.UtcNow, new[] { new DetectedListDto("node-1", "List", 1, 0, new[] { "Item" }, false, "List", "High") }, new[] { new DetectedTargetDto("node-2", "Target", true, false, false, 1, 0, "Target", "High") }, new[] { new DetectedAlertDto("node-3", "Alert", "Medium", true, null, "High") }, new[] { new DetectedTransitStateDto("Transit", new[] { "node-4" }, "Transit", 0.5, new[] { "Reason" }, "High") }, new[] { new DetectedResourceDto("node-5", "Energy", "Resource", 0.7, 10, false, false, "High") }, new[] { new DetectedCapabilityDto("node-6", "Act", "Enabled", true, true, false, "High") }, new[] { new DetectedPresenceEntityDto("node-7", "Presence", 1, new[] { "Membership" }, "Presence", "Active", "High") }, Array.Empty<string>(), new Dictionary<string, string> { ["confidence"] = "High" }));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/semantic/summary")
        {
            return Json(new SemanticSummaryDto("alpha", DateTimeOffset.UtcNow, 1, 1, 1, 1, 1, 1, 1, Array.Empty<string>(), new Dictionary<string, string> { ["confidence"] = "High" }));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/domain")
        {
            return Json(new SessionDomainStateDto("alpha", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, "Projection", new NavigationStateDto("Idle", false, null, null, null, null, null), new CombatStateDto("Idle", null, false, false, null, null), new ThreatStateDto("Low", 0, 0, 1, true, null, Array.Empty<string>(), "Withdraw", null, null), new TargetStateDto(true, "target-1", "Target", 1, 1, 1, "Active", null, null), new CompanionStateDto("Idle", true, true, 1, 1, 0, 0, null), new ResourceStateDto(90, 80, 70, 1, 1, false, false, null), new LocationStateDto("Base", "Hangar", true, false, "High", null, null), Array.Empty<string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/memory")
        {
            return Json(new SessionOperationalMemorySnapshotDto("alpha", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new SessionOperationalMemorySummaryDto(1, 1, 1, 1, 1, DateTimeOffset.UtcNow, "High", "Success"), Array.Empty<WorksiteObservationDto>(), Array.Empty<RiskObservationDto>(), Array.Empty<PresenceObservationDto>(), Array.Empty<TimingObservationDto>(), Array.Empty<OutcomeObservationDto>(), Array.Empty<string>(), new Dictionary<string, string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/memory/context")
        {
            return Json(new PolicyMemoryContextDto("alpha", DateTimeOffset.UtcNow, Array.Empty<WorksiteMemorySummaryDto>(), new RiskMemorySummaryDto("High", 1, 0, new[] { "source" }, new[] { "Withdraw" }, false, new Dictionary<string, string>()), new PresenceMemorySummaryDto(1, 1, DateTimeOffset.UtcNow, true, new Dictionary<string, string>()), new TimingMemorySummaryDto(new[] { "wait" }, 10, 10, 10, false, new Dictionary<string, string>()), new OutcomeMemorySummaryDto("Success", 1, 0, 0, 0, 0, false, new Dictionary<string, string>()), Array.Empty<string>(), new Dictionary<string, string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/persistence")
        {
            return Json(new RuntimePersistenceSessionStatusDto("alpha", true, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, null, "/tmp/alpha.json", 1, 1, 1, 1, 1));
        }

        if (method == HttpMethod.Get && path == "/persistence")
        {
            return Json(new RuntimePersistenceStatusDto(true, "Json", "./data", 1, DateTimeOffset.UtcNow, new[] { new RuntimePersistenceSessionStatusDto("alpha", true, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, null, "/tmp/alpha.json", 1, 1, 1, 1, 1) }));
        }

        if (method == HttpMethod.Get && path == "/targets")
        {
            return Json(new[] { new DesktopTargetProfileDto("test-app", "Desktop", "TestApp.exe", null, null, null, "Exact", new Dictionary<string, string?>(), true, true) });
        }

        if (method == HttpMethod.Get && path == "/bindings")
        {
            return Json(new BindingStoreSnapshotDto(1, DateTimeOffset.UtcNow, new[] { new SessionTargetBindingDto("alpha", "test-app", new Dictionary<string, string> { ["Port"] = "7100" }, null) }));
        }

        if (method == HttpMethod.Get && path == "/coordination")
        {
            return Json(new ExecutionCoordinationSnapshotDto(DateTimeOffset.UtcNow, Array.Empty<ActiveExecutionEntryDto>(), Array.Empty<WaitingExecutionEntryDto>(), Array.Empty<ExecutionResourceStateDto>(), 0, 0, 0, Array.Empty<ExecutionContentionStatDto>()));
        }

        if (method == HttpMethod.Get && path == "/policy-rules")
        {
            return Json(new PolicyRuleSetDto(Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>(), Array.Empty<PolicyRuleDto>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/policy-state")
        {
            return Json(new SessionPolicyControlStateDto("alpha", true, DateTimeOffset.UtcNow.AddMinutes(-4), null, DateTimeOffset.UtcNow.AddMinutes(-3), "paused", "operator", "tester", new Dictionary<string, string>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/decision-execution/history")
        {
            return Json(new DecisionPlanExecutionHistoryDto("alpha", Array.Empty<DecisionPlanExecutionHistoryEntryDto>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/decision-plan/history")
        {
            return Json(new DecisionPlanHistoryDto("alpha", Array.Empty<DecisionPlanHistoryEntryDto>()));
        }

        if (method == HttpMethod.Get && path == "/sessions/alpha/policy-state/history")
        {
            return Json(Array.Empty<SessionPolicyControlHistoryEntryDto>());
        }

        if (method == HttpMethod.Post && path is "/sessions/alpha/pause-policy" or "/sessions/alpha/resume-policy")
        {
            return Json(new PolicyControlActionResultDto("alpha", path.EndsWith("pause-policy", StringComparison.OrdinalIgnoreCase) ? "Pause" : "Resume", true, new SessionPolicyControlStateDto("alpha", path.EndsWith("pause-policy", StringComparison.OrdinalIgnoreCase), DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, null, null, new Dictionary<string, string>()), Array.Empty<SessionPolicyControlHistoryEntryDto>(), null));
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/decision-plan/evaluate")
        {
            return Json(new DecisionPlanDto("alpha", DateTimeOffset.UtcNow, "Ready", Array.Empty<DecisionDirectiveDto>(), Array.Empty<DecisionReasonDto>(), new PolicyExecutionSummaryDto(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), 0, 0, new Dictionary<string, int>()), Array.Empty<string>()));
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/decision-plan/execute")
        {
            return Json(new DecisionPlanExecutionDto("alpha", "fingerprint", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "Succeeded", false, Array.Empty<DecisionDirectiveExecutionResultDto>(), new DecisionPlanExecutionSummaryDto(1, 1, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), null, null, Array.Empty<string>(), new Dictionary<string, string>()));
        }

        if (method == HttpMethod.Put && path == "/bindings/alpha")
        {
            return Json(new SessionTargetBindingDto("alpha", "updated-profile", new Dictionary<string, string> { ["Port"] = "7200" }, null));
        }

        if (method == HttpMethod.Delete && path == "/bindings/alpha")
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/commands")
        {
            return Json(new UiCommandResultDto(true, "alpha", "node-1", "invoke", "ok", DateTimeOffset.UtcNow, true, null));
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/start")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/stop")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/pause")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/resume")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (method == HttpMethod.Post && path == "/sessions/alpha/ui/refresh")
        {
            return Json(new SessionUiRefreshDto("alpha", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, null, null, null, null, Array.Empty<PlannedUiWorkItem>()));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"Not mapped\"}", Encoding.UTF8, "application/json")
        };
    }

    private static SessionInfoDto SampleSessionInfo() =>
        new(
            "alpha",
            "Alpha Session",
            true,
            ["operator"],
            new SessionStateDto("Running", "Running", "Running", 0, 0, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-2), null, null),
            new SessionMetricsDto(1, 0, 0, 1));

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Body)
    {
        public static CapturedRequest From(HttpRequestMessage request)
        {
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new CapturedRequest(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body);
        }
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

        public ScriptedHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
