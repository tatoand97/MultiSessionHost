namespace MultiSessionHost.Desktop.Models;

public sealed record TestDesktopAppState(
    string SessionId,
    string Status,
    string Notes,
    bool Enabled,
    IReadOnlyList<string> Items,
    int TickCount,
    int Port,
    int ProcessId,
    long WindowHandle,
    string WindowTitle,
    DateTimeOffset CapturedAtUtc);
