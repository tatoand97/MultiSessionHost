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
            Sessions = sessions
        };

    public static SessionHostOptions CreateDesktopTestAppOptions(int basePort, params SessionDefinitionOptions[] sessions) =>
        new()
        {
            MaxGlobalParallelSessions = sessions.Length,
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            EnableAdminApi = false,
            AdminApiUrl = "http://localhost:5088",
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
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
}
