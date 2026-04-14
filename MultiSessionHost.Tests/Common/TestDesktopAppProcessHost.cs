using System.Diagnostics;
using System.Net.Http.Json;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Tests.Common;

public sealed class TestDesktopAppProcessHost : IAsyncDisposable
{
    private readonly Process _process;

    private TestDesktopAppProcessHost(string sessionId, int port, Process process, HttpClient client)
    {
        SessionId = sessionId;
        Port = port;
        _process = process;
        Client = client;
    }

    public string SessionId { get; }

    public int Port { get; }

    public int ProcessId => _process.Id;

    public HttpClient Client { get; }

    public Uri BaseAddress => Client.BaseAddress!;

    public static async Task<TestDesktopAppProcessHost> StartAsync(string sessionId, int port)
    {
        var executablePath = GetExecutablePath();
        var startInfo = new ProcessStartInfo(executablePath, $"--session-id {sessionId} --port {port}")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executablePath}'.");
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var host = new TestDesktopAppProcessHost(sessionId, port, process, client);

        await TestWait.UntilAsync(
            async () =>
            {
                try
                {
                    var state = await host.GetStateAsync().ConfigureAwait(false);
                    return string.Equals(state.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
                        state.Port == port &&
                        state.ProcessId == process.Id &&
                        state.WindowHandle != 0;
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(10),
            $"The test desktop app '{sessionId}' on port {port} did not become ready in time.").ConfigureAwait(false);

        return host;
    }

    public async Task<TestDesktopAppState> GetStateAsync() =>
        await Client.GetFromJsonAsync<TestDesktopAppState>("state").ConfigureAwait(false)
        ?? throw new InvalidOperationException($"The test desktop app '{SessionId}' returned an empty state payload.");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (_process.HasExited)
        {
            _process.Dispose();
            return;
        }

        try
        {
            _process.CloseMainWindow();
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static string GetExecutablePath()
    {
        var assemblyPath = typeof(MultiSessionHost.TestDesktopApp.Program).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException("Could not resolve the test desktop app output directory.");
        var executablePath = Path.Combine(directory, "MultiSessionHost.TestDesktopApp.exe");

        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException($"The test desktop app executable was not found at '{executablePath}'.");
        }

        return executablePath;
    }
}
