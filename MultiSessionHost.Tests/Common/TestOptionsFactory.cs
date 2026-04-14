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
            DesktopSessionMatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
            TestAppBasePort = 7100,
            EnableUiSnapshots = false,
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
}
