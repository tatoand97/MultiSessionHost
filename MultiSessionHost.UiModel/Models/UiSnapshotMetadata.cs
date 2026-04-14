namespace MultiSessionHost.UiModel.Models;

public sealed record UiSnapshotMetadata(
    string SessionId,
    string Source,
    DateTimeOffset CapturedAtUtc,
    int ProcessId,
    long WindowHandle,
    string? WindowTitle,
    IReadOnlyDictionary<string, string?> Properties);
