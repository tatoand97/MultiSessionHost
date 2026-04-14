namespace MultiSessionHost.TestDesktopApp;

public sealed record TestDesktopAppOptions(
    string SessionId,
    int Port)
{
    public static TestDesktopAppOptions Parse(string[] args)
    {
        string? sessionId = null;
        int? port = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--session-id" when index + 1 < args.Length:
                    sessionId = args[++index];
                    break;

                case "--port" when index + 1 < args.Length && int.TryParse(args[++index], out var parsedPort):
                    port = parsedPort;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Missing required argument '--session-id <id>'.");
        }

        if (port is null or <= 0 or > 65535)
        {
            throw new InvalidOperationException("Missing or invalid required argument '--port <port>'.");
        }

        return new TestDesktopAppOptions(sessionId.Trim(), port.Value);
    }
}
