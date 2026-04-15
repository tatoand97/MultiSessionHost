using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Tests.Common;

public static class TestOptionsFactory
{
    public static SessionHostOptions Create(params SessionDefinitionOptions[] sessions) =>
        new()
        {
            MaxGlobalParallelSessions = 4,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = false,
            AdminApiUrl = "http://localhost:5088",
            DriverMode = DriverMode.NoOp,
            EnableUiSnapshots = false,
            RuntimePersistence = TestRuntimePersistenceOptions(),
            Sessions = sessions
        };

    public static SessionHostOptions CreateDesktopTestAppOptions(int basePort, params SessionDefinitionOptions[] sessions) =>
        CreateDesktopTestAppOptions(basePort, false, "http://localhost:5088", sessions);

    public static SessionHostOptions CreateDesktopTestAppOptions(
        int basePort,
        bool enableAdminApi,
        string adminApiUrl,
        params SessionDefinitionOptions[] sessions) =>
        new()
        {
            MaxGlobalParallelSessions = sessions.Length,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = enableAdminApi,
            AdminApiUrl = adminApiUrl,
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            RuntimePersistence = TestRuntimePersistenceOptions(),
            DesktopTargets = [DesktopTestAppProfile()],
            SessionTargetBindings = sessions
                .Select(
                    (session, index) => SessionTargetBinding(
                        session.SessionId,
                        "test-app",
                        (basePort + index).ToString()))
                .ToArray(),
            Sessions = sessions
        };

    public static SessionHostOptions CreateDesktopTestAppOptionsWithRisk(
        int basePort,
        bool enableAdminApi,
        string adminApiUrl,
        RiskClassificationOptions riskClassification,
        params SessionDefinitionOptions[] sessions) =>
        new()
        {
            MaxGlobalParallelSessions = sessions.Length,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = enableAdminApi,
            AdminApiUrl = adminApiUrl,
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            RuntimePersistence = TestRuntimePersistenceOptions(),
            RiskClassification = riskClassification,
            DesktopTargets = [DesktopTestAppProfile()],
            SessionTargetBindings = sessions
                .Select(
                    (session, index) => SessionTargetBinding(
                        session.SessionId,
                        "test-app",
                        (basePort + index).ToString()))
                .ToArray(),
            Sessions = sessions
        };

    public static SessionDefinitionOptions Session(
        string id,
        bool enabled = true,
        int tickIntervalMs = 100,
        int startupDelayMs = 0,
        int maxParallelWorkItems = 1,
        int maxRetryCount = 3,
        int initialBackoffMs = 100,
        params string[] tags) =>
        new()
        {
            SessionId = id,
            DisplayName = $"{id}-display",
            Enabled = enabled,
            TickIntervalMs = tickIntervalMs,
            StartupDelayMs = startupDelayMs,
            MaxParallelWorkItems = maxParallelWorkItems,
            MaxRetryCount = maxRetryCount,
            InitialBackoffMs = initialBackoffMs,
            Tags = tags
        };

    public static DesktopTargetProfileOptions DesktopTestAppProfile(string profileName = "test-app") =>
        new()
        {
            ProfileName = profileName,
            Kind = DesktopTargetKind.DesktopTestApp,
            ProcessName = "MultiSessionHost.TestDesktopApp",
            WindowTitleFragment = "[SessionId: {SessionId}]",
            CommandLineFragmentTemplate = "--session-id {SessionId}",
            BaseAddressTemplate = "http://127.0.0.1:{Port}/",
            MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
            SupportsUiSnapshots = true,
            SupportsStateEndpoint = true,
            Metadata = new Dictionary<string, string?>
            {
                ["UiSource"] = "DesktopTestApp"
            }
        };

    public static SessionTargetBindingOptions SessionTargetBinding(
        string sessionId,
        string targetProfileName,
        string port,
        DesktopTargetProfileOverrideOptions? overrides = null) =>
        new()
        {
            SessionId = sessionId,
            TargetProfileName = targetProfileName,
            Variables = new Dictionary<string, string?>
            {
                ["Port"] = port
            },
            Overrides = overrides
        };

    public static RiskClassificationOptions GenericRiskClassification() =>
        new()
        {
            EnableRiskClassification = true,
            DefaultUnknownDisposition = RiskDisposition.Unknown,
            DefaultUnknownSeverity = RiskSeverity.Unknown,
            DefaultUnknownPolicy = RiskPolicySuggestion.Observe,
            MaxReturnedEntities = 50,
            RequireExplicitSafeMatch = true,
            Rules =
            [
                new RiskRuleOptions
                {
                    RuleName = "safe-presence",
                    MatchByName = ["presence"],
                    NameMatchMode = RiskRuleMatchMode.Contains,
                    Disposition = RiskDisposition.Safe,
                    Severity = RiskSeverity.Low,
                    Priority = 10,
                    SuggestedPolicy = RiskPolicySuggestion.Ignore,
                    Reason = "Presence-labeled entities are safe in this test configuration."
                },
                new RiskRuleOptions
                {
                    RuleName = "priority-selection",
                    MatchByTags = ["active"],
                    Disposition = RiskDisposition.Threat,
                    Severity = RiskSeverity.High,
                    Priority = 900,
                    SuggestedPolicy = RiskPolicySuggestion.Prioritize,
                    Reason = "Selected item-2 is prioritized in this test configuration."
                },
                new RiskRuleOptions
                {
                    RuleName = "warning-alert",
                    MatchByType = ["Warning", "Critical"],
                    TypeMatchMode = RiskRuleMatchMode.Exact,
                    Disposition = RiskDisposition.Threat,
                    Severity = RiskSeverity.Critical,
                    Priority = 800,
                    SuggestedPolicy = RiskPolicySuggestion.Withdraw,
                    Reason = "Warning and critical alert types require withdrawal in this test configuration."
                },
                new RiskRuleOptions
                {
                    RuleName = "unknown-tag",
                    MatchByTags = ["unknown"],
                    Disposition = RiskDisposition.Unknown,
                    Severity = RiskSeverity.Low,
                    Priority = 100,
                    SuggestedPolicy = RiskPolicySuggestion.Observe,
                    Reason = "Unknown-tagged candidates should be observed."
                }
            ]
        };

    public static RuntimePersistenceOptions TestRuntimePersistenceOptions(string? basePath = null) =>
        new()
        {
            EnableRuntimePersistence = true,
            Mode = RuntimePersistenceMode.JsonFile,
            BasePath = basePath ?? Path.Combine(Path.GetTempPath(), "MultiSessionHost.Tests", Guid.NewGuid().ToString("N")),
            AutoFlushAfterStateChanges = true,
            FailOnPersistenceErrors = false
        };
}
