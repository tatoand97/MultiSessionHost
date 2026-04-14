using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Configuration;

public sealed class SessionHostOptions
{
    public const string SectionName = "MultiSessionHost";

    public int MaxGlobalParallelSessions { get; init; } = 4;

    public int SchedulerIntervalMs { get; init; } = 500;

    public int HealthLogIntervalMs { get; init; } = 5_000;

    public bool EnableAdminApi { get; init; }

    public string AdminApiUrl { get; init; } = "http://localhost:5088";

    public DriverMode DriverMode { get; init; } = DriverMode.NoOp;

    public DesktopSessionMatchingMode DesktopSessionMatchingMode { get; init; } = DesktopSessionMatchingMode.WindowTitleAndCommandLine;

    public int TestAppBasePort { get; init; } = 7100;

    public bool EnableUiSnapshots { get; init; }

    public IReadOnlyList<SessionDefinitionOptions> Sessions { get; init; } = [];
}

public sealed class SessionDefinitionOptions
{
    public string SessionId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int TickIntervalMs { get; init; } = 1_000;

    public int StartupDelayMs { get; init; } = 250;

    public int MaxParallelWorkItems { get; init; } = 1;

    public int MaxRetryCount { get; init; } = 3;

    public int InitialBackoffMs { get; init; } = 1_000;

    public IReadOnlyList<string> Tags { get; init; } = [];
}
