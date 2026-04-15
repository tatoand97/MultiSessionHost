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

    public bool EnableUiSnapshots { get; init; }

    public IReadOnlyList<DesktopTargetProfileOptions> DesktopTargets { get; init; } = [];

    public IReadOnlyList<SessionTargetBindingOptions> SessionTargetBindings { get; init; } = [];

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

public sealed class DesktopTargetProfileOptions
{
    public string ProfileName { get; init; } = string.Empty;

    public DesktopTargetKind Kind { get; init; } = DesktopTargetKind.SelfHostedHttpDesktop;

    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitleFragment { get; init; }

    public string? CommandLineFragmentTemplate { get; init; }

    public string? BaseAddressTemplate { get; init; }

    public DesktopSessionMatchingMode MatchingMode { get; init; } = DesktopSessionMatchingMode.WindowTitleAndCommandLine;

    public Dictionary<string, string?> Metadata { get; init; } = [];

    public bool SupportsUiSnapshots { get; init; }

    public bool SupportsStateEndpoint { get; init; }
}

public sealed class SessionTargetBindingOptions
{
    public string SessionId { get; init; } = string.Empty;

    public string TargetProfileName { get; init; } = string.Empty;

    public Dictionary<string, string?> Variables { get; init; } = [];

    public DesktopTargetProfileOverrideOptions? Overrides { get; init; }
}

public sealed class DesktopTargetProfileOverrideOptions
{
    public string? ProcessName { get; init; }

    public string? WindowTitleFragment { get; init; }

    public string? CommandLineFragmentTemplate { get; init; }

    public string? BaseAddressTemplate { get; init; }

    public DesktopSessionMatchingMode? MatchingMode { get; init; }

    public Dictionary<string, string?> Metadata { get; init; } = [];

    public bool? SupportsUiSnapshots { get; init; }

    public bool? SupportsStateEndpoint { get; init; }
}
