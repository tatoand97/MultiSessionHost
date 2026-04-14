using System.Text.RegularExpressions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class DefaultSessionAttachmentResolver : ISessionAttachmentResolver
{
    private static readonly Regex PortRegex = new("--port\\s+(?<port>\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly SessionHostOptions _options;
    private readonly IProcessLocator _processLocator;
    private readonly IWindowLocator _windowLocator;
    private readonly IClock _clock;

    public DefaultSessionAttachmentResolver(
        SessionHostOptions options,
        IProcessLocator processLocator,
        IWindowLocator windowLocator,
        IClock clock)
    {
        _options = options;
        _processLocator = processLocator;
        _windowLocator = windowLocator;
        _clock = clock;
    }

    public ValueTask<DesktopSessionAttachment> ResolveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var expectedPort = ResolveExpectedPort(snapshot.SessionId);
        var target = new DesktopSessionTarget(
            snapshot.SessionId,
            ProcessName: "MultiSessionHost.TestDesktopApp",
            WindowTitleFragment: $"[SessionId: {snapshot.SessionId.Value}]",
            CommandLineFragment: $"--session-id {snapshot.SessionId.Value}",
            ExpectedPort: expectedPort,
            BaseAddress: new Uri($"http://127.0.0.1:{expectedPort}/", UriKind.Absolute));

        var processes = _processLocator.GetProcesses(target.ProcessName);

        if (processes.Count == 0)
        {
            processes = _processLocator.GetProcesses();
        }

        var windows = _windowLocator.GetWindows();
        var windowMatches = windows.Where(window => MatchesWindow(window, target)).ToArray();
        var processMatches = processes.Where(process => MatchesProcess(process, target, windowMatches)).ToArray();
        var selectedProcess = SelectProcess(processMatches, windowMatches, target);
        var selectedWindow = SelectWindow(windowMatches, selectedProcess, target);
        var baseAddress = ResolveBaseAddress(selectedProcess, target);

        return ValueTask.FromResult(
            new DesktopSessionAttachment(
                snapshot.SessionId,
                target,
                selectedProcess,
                selectedWindow,
                baseAddress,
                _clock.UtcNow));
    }

    private DesktopProcessInfo SelectProcess(
        IReadOnlyList<DesktopProcessInfo> processMatches,
        IReadOnlyList<DesktopWindowInfo> windowMatches,
        DesktopSessionTarget target)
    {
        DesktopProcessInfo[] candidates = _options.DesktopSessionMatchingMode switch
        {
            DesktopSessionMatchingMode.WindowTitle => processMatches.Where(process => windowMatches.Any(window => window.ProcessId == process.ProcessId)).ToArray(),
            DesktopSessionMatchingMode.CommandLine => processMatches.ToArray(),
            DesktopSessionMatchingMode.WindowTitleAndCommandLine => processMatches.Where(process => windowMatches.Any(window => window.ProcessId == process.ProcessId)).ToArray(),
            _ => throw new InvalidOperationException($"DesktopSessionMatchingMode '{_options.DesktopSessionMatchingMode}' is not supported.")
        };

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"Could not resolve a desktop process for session '{target.SessionId}'."),
            _ => throw new InvalidOperationException($"Multiple desktop processes matched session '{target.SessionId}'.")
        };
    }

    private static DesktopWindowInfo SelectWindow(
        IReadOnlyList<DesktopWindowInfo> windowMatches,
        DesktopProcessInfo selectedProcess,
        DesktopSessionTarget target)
    {
        var candidates = windowMatches
            .Where(window => window.ProcessId == selectedProcess.ProcessId)
            .OrderByDescending(window => window.WindowHandle == selectedProcess.MainWindowHandle)
            .ThenByDescending(static window => window.IsVisible)
            .ThenBy(static window => window.WindowHandle)
            .ToArray();

        return candidates.Length == 0
            ? throw new InvalidOperationException($"Could not resolve a window for session '{target.SessionId}'.")
            : candidates[0];
    }

    private bool MatchesProcess(DesktopProcessInfo process, DesktopSessionTarget target, IReadOnlyList<DesktopWindowInfo> windowMatches)
    {
        var matchesCommandLine = process.CommandLine is not null &&
            process.CommandLine.Contains(target.CommandLineFragment, StringComparison.OrdinalIgnoreCase);
        var matchesWindowTitle = windowMatches.Any(window => window.ProcessId == process.ProcessId);

        return _options.DesktopSessionMatchingMode switch
        {
            DesktopSessionMatchingMode.WindowTitle => matchesWindowTitle,
            DesktopSessionMatchingMode.CommandLine => matchesCommandLine,
            DesktopSessionMatchingMode.WindowTitleAndCommandLine => matchesWindowTitle && matchesCommandLine,
            _ => false
        };
    }

    private static bool MatchesWindow(DesktopWindowInfo window, DesktopSessionTarget target) =>
        window.IsVisible &&
        window.Title.Contains(target.WindowTitleFragment, StringComparison.OrdinalIgnoreCase);

    private static Uri ResolveBaseAddress(DesktopProcessInfo process, DesktopSessionTarget target)
    {
        if (process.CommandLine is not null)
        {
            var match = PortRegex.Match(process.CommandLine);

            if (match.Success && int.TryParse(match.Groups["port"].Value, out var port))
            {
                return new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
            }
        }

        return target.BaseAddress;
    }

    private int ResolveExpectedPort(SessionId sessionId)
    {
        var index = _options.Sessions
            .Select(static (session, idx) => new { session.SessionId, Index = idx })
            .FirstOrDefault(entry => string.Equals(entry.SessionId, sessionId.Value, StringComparison.OrdinalIgnoreCase))
            ?.Index
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found in configuration.");

        return _options.TestAppBasePort + index;
    }
}
