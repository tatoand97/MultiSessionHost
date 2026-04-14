using System.Diagnostics;
using System.Management;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Processes;

public sealed class Win32ProcessLocator : IProcessLocator
{
    public IReadOnlyCollection<DesktopProcessInfo> GetProcesses(string? processName = null)
    {
        var commandLines = GetCommandLines(processName);

        return Process
            .GetProcesses()
            .Where(process => string.IsNullOrWhiteSpace(processName) || string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            .Select(
                process =>
                {
                    commandLines.TryGetValue(process.Id, out var commandLine);
                    return new DesktopProcessInfo(process.Id, process.ProcessName, commandLine, SafeGetMainWindowHandle(process));
                })
            .OrderBy(static process => process.ProcessId)
            .ToArray();
    }

    public DesktopProcessInfo? GetProcessById(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var commandLines = GetCommandLines(process.ProcessName);
            commandLines.TryGetValue(processId, out var commandLine);
            return new DesktopProcessInfo(process.Id, process.ProcessName, commandLine, SafeGetMainWindowHandle(process));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<int, string?> GetCommandLines(string? processName)
    {
        try
        {
            var query = string.IsNullOrWhiteSpace(processName)
                ? "SELECT ProcessId, CommandLine FROM Win32_Process"
                : $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{NormalizeExecutableName(processName)}'";

            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();

            return results
                .Cast<ManagementObject>()
                .Select(
                    result => new
                    {
                        ProcessId = Convert.ToInt32(result["ProcessId"], System.Globalization.CultureInfo.InvariantCulture),
                        CommandLine = result["CommandLine"]?.ToString()
                    })
                .ToDictionary(static item => item.ProcessId, static item => item.CommandLine);
        }
        catch
        {
            return new Dictionary<int, string?>();
        }
    }

    private static string NormalizeExecutableName(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";

    private static long SafeGetMainWindowHandle(Process process)
    {
        try
        {
            return process.MainWindowHandle.ToInt64();
        }
        catch
        {
            return 0;
        }
    }
}
