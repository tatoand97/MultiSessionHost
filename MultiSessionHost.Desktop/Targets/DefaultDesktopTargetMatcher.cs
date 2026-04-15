using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class DefaultDesktopTargetMatcher : IDesktopTargetMatcher
{
    public (DesktopProcessInfo Process, DesktopWindowInfo Window) Match(
        IReadOnlyList<DesktopProcessInfo> processes,
        IReadOnlyList<DesktopWindowInfo> windows,
        DesktopSessionTarget target)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(target);

        var visibleWindows = windows.Where(static window => window.IsVisible).ToArray();
        var processMatches = processes
            .Where(process => MatchesProcess(process, visibleWindows, target))
            .ToArray();
        var selectedProcess = SelectProcess(processMatches, visibleWindows, target);
        var selectedWindow = SelectWindow(visibleWindows, selectedProcess, target);

        return (selectedProcess, selectedWindow);
    }

    private static DesktopProcessInfo SelectProcess(
        IReadOnlyList<DesktopProcessInfo> processMatches,
        IReadOnlyList<DesktopWindowInfo> visibleWindows,
        DesktopSessionTarget target)
    {
        DesktopProcessInfo[] candidates = target.MatchingMode switch
        {
            DesktopSessionMatchingMode.WindowTitle => processMatches.Where(process => HasMatchingWindow(process, visibleWindows, target.WindowTitleFragment)).ToArray(),
            DesktopSessionMatchingMode.CommandLine => processMatches.ToArray(),
            DesktopSessionMatchingMode.WindowTitleAndCommandLine => processMatches.Where(process => HasMatchingWindow(process, visibleWindows, target.WindowTitleFragment)).ToArray(),
            _ => throw new InvalidOperationException($"DesktopSessionMatchingMode '{target.MatchingMode}' is not supported.")
        };

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"Could not resolve a desktop process for session '{target.SessionId}'."),
            _ => throw new InvalidOperationException($"Multiple desktop processes matched session '{target.SessionId}'.")
        };
    }

    private static DesktopWindowInfo SelectWindow(
        IReadOnlyList<DesktopWindowInfo> windows,
        DesktopProcessInfo selectedProcess,
        DesktopSessionTarget target)
    {
        var candidates = windows
            .Where(window => window.ProcessId == selectedProcess.ProcessId && MatchesWindow(window, target.WindowTitleFragment))
            .OrderByDescending(window => window.WindowHandle == selectedProcess.MainWindowHandle)
            .ThenBy(static window => window.WindowHandle)
            .ToArray();

        return candidates.Length == 0
            ? throw new InvalidOperationException($"Could not resolve a window for session '{target.SessionId}'.")
            : candidates[0];
    }

    private static bool MatchesProcess(
        DesktopProcessInfo process,
        IReadOnlyList<DesktopWindowInfo> visibleWindows,
        DesktopSessionTarget target)
    {
        var matchesCommandLine = !string.IsNullOrWhiteSpace(target.CommandLineFragment) &&
            process.CommandLine?.Contains(target.CommandLineFragment, StringComparison.OrdinalIgnoreCase) == true;
        var matchesWindow = HasMatchingWindow(process, visibleWindows, target.WindowTitleFragment);

        return target.MatchingMode switch
        {
            DesktopSessionMatchingMode.WindowTitle => matchesWindow,
            DesktopSessionMatchingMode.CommandLine => matchesCommandLine,
            DesktopSessionMatchingMode.WindowTitleAndCommandLine => matchesWindow && matchesCommandLine,
            _ => false
        };
    }

    private static bool HasMatchingWindow(
        DesktopProcessInfo process,
        IReadOnlyList<DesktopWindowInfo> visibleWindows,
        string? windowTitleFragment) =>
        visibleWindows.Any(window => window.ProcessId == process.ProcessId && MatchesWindow(window, windowTitleFragment));

    private static bool MatchesWindow(DesktopWindowInfo window, string? windowTitleFragment) =>
        string.IsNullOrWhiteSpace(windowTitleFragment) ||
        window.Title.Contains(windowTitleFragment, StringComparison.OrdinalIgnoreCase);
}
