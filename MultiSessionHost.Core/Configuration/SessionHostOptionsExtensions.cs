using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Configuration;

public static class SessionHostOptionsExtensions
{
    public static IReadOnlyList<SessionDefinition> ToSessionDefinitions(this SessionHostOptions options) =>
        options.Sessions
            .Select(
                session => new SessionDefinition(
                    new SessionId(session.SessionId),
                    session.DisplayName.Trim(),
                    session.Enabled,
                    TimeSpan.FromMilliseconds(session.TickIntervalMs),
                    TimeSpan.FromMilliseconds(session.StartupDelayMs),
                    session.MaxParallelWorkItems,
                    session.MaxRetryCount,
                    TimeSpan.FromMilliseconds(session.InitialBackoffMs),
                    session.Tags
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(static tag => tag.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
            .ToArray();

    public static bool TryValidate(this SessionHostOptions options, out string? error)
    {
        if (options.MaxGlobalParallelSessions <= 0)
        {
            error = "MaxGlobalParallelSessions must be greater than zero.";
            return false;
        }

        if (options.SchedulerIntervalMs <= 0)
        {
            error = "SchedulerIntervalMs must be greater than zero.";
            return false;
        }

        if (options.HealthLogIntervalMs <= 0)
        {
            error = "HealthLogIntervalMs must be greater than zero.";
            return false;
        }

        if (options.EnableAdminApi && !Uri.TryCreate(options.AdminApiUrl, UriKind.Absolute, out _))
        {
            error = "AdminApiUrl must be a valid absolute URL when EnableAdminApi is true.";
            return false;
        }

        if (!Enum.IsDefined(options.DriverMode))
        {
            error = $"DriverMode '{options.DriverMode}' is not valid.";
            return false;
        }

        if (!Enum.IsDefined(options.DesktopSessionMatchingMode))
        {
            error = $"DesktopSessionMatchingMode '{options.DesktopSessionMatchingMode}' is not valid.";
            return false;
        }

        if (options.TestAppBasePort is <= 0 or > 65535)
        {
            error = "TestAppBasePort must be between 1 and 65535.";
            return false;
        }

        if (options.EnableUiSnapshots && options.DriverMode != DriverMode.DesktopTestApp)
        {
            error = "EnableUiSnapshots requires DriverMode=DesktopTestApp.";
            return false;
        }

        if (options.Sessions.Count == 0)
        {
            error = "At least one session must be configured.";
            return false;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in options.Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.SessionId))
            {
                error = "Each session must have a non-empty SessionId.";
                return false;
            }

            if (!seenIds.Add(session.SessionId.Trim()))
            {
                error = $"SessionId '{session.SessionId}' is duplicated.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(session.DisplayName))
            {
                error = $"Session '{session.SessionId}' must have a DisplayName.";
                return false;
            }

            if (session.TickIntervalMs <= 0)
            {
                error = $"Session '{session.SessionId}' must have TickIntervalMs greater than zero.";
                return false;
            }

            if (session.StartupDelayMs < 0)
            {
                error = $"Session '{session.SessionId}' cannot have a negative StartupDelayMs.";
                return false;
            }

            if (session.MaxParallelWorkItems <= 0)
            {
                error = $"Session '{session.SessionId}' must have MaxParallelWorkItems greater than zero.";
                return false;
            }

            if (session.MaxRetryCount < 0)
            {
                error = $"Session '{session.SessionId}' cannot have a negative MaxRetryCount.";
                return false;
            }

            if (session.InitialBackoffMs <= 0)
            {
                error = $"Session '{session.SessionId}' must have InitialBackoffMs greater than zero.";
                return false;
            }
        }

        if (options.DriverMode == DriverMode.DesktopTestApp)
        {
            var maxPort = options.TestAppBasePort + options.Sessions.Count - 1;

            if (maxPort > 65535)
            {
                error = "TestAppBasePort plus configured session count exceeds the maximum TCP port.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
