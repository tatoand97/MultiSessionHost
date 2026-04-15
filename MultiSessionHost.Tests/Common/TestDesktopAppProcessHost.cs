using System.Diagnostics;
using System.Net.Http.Json;

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
        await StopExistingTestAppOnPortAsync(port).ConfigureAwait(false);

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

        try
        {
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
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return host;
    }

    public async Task<TestDesktopAppState> GetStateAsync() =>
        await Client.GetFromJsonAsync<TestDesktopAppState>("state").ConfigureAwait(false)
        ?? throw new InvalidOperationException($"The test desktop app '{SessionId}' returned an empty state payload.");

    public async Task SetArtificialDelayAsync(int milliseconds)
    {
        using var response = await Client.PostAsJsonAsync("test/delay", new { milliseconds }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        try
        {
            using var shutdownClient = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{Port}/", UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(2)
            };

            using var shutdownResponse = await shutdownClient.PostAsync("shutdown", content: null).ConfigureAwait(false);
            shutdownResponse.EnsureSuccessStatusCode();
        }
        catch
        {
        }

        if (_process.HasExited)
        {
            _process.Dispose();
            return;
        }

        try
        {
            try
            {
                if (_process.CloseMainWindow())
                {
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static async Task StopExistingTestAppOnPortAsync(int port)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(1)
        };

        try
        {
            var state = await client.GetFromJsonAsync<TestDesktopAppState>("state").ConfigureAwait(false);

            if (state is null ||
                state.Port != port ||
                !state.WindowTitle.StartsWith("MultiSessionHost.TestDesktopApp", StringComparison.Ordinal))
            {
                return;
            }

            using var shutdownResponse = await client.PostAsync("shutdown", content: null).ConfigureAwait(false);
            shutdownResponse.EnsureSuccessStatusCode();

            await TestWait.UntilAsync(
                async () =>
                {
                    try
                    {
                        using var response = await client.GetAsync("state").ConfigureAwait(false);
                        return !response.IsSuccessStatusCode;
                    }
                    catch
                    {
                        return true;
                    }
                },
                TimeSpan.FromSeconds(3),
                $"The existing test desktop app on port {port} did not shut down in time.").ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string GetExecutablePath()
    {
        var executablePath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "MultiSessionHost.TestDesktopApp",
                "bin",
                "Debug",
                "net10.0-windows",
                "MultiSessionHost.TestDesktopApp.exe"));

        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException($"The test desktop app executable was not found at '{executablePath}'.");
        }

        return executablePath;
    }
}

public sealed record TestDesktopAppState(
    string SessionId,
    string Status,
    string Notes,
    bool Enabled,
    string? SelectedItem,
    IReadOnlyList<string> Items,
    int TickCount,
    int Port,
    int ProcessId,
    long WindowHandle,
    string WindowTitle,
    DateTimeOffset CapturedAt);
