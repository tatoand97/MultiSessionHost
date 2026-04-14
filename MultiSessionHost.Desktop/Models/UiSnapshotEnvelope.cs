using System.Text.Json;

namespace MultiSessionHost.Desktop.Models;

public sealed record UiSnapshotEnvelope(
    string SessionId,
    DateTimeOffset CapturedAtUtc,
    DesktopProcessInfo Process,
    DesktopWindowInfo Window,
    JsonElement Root,
    IReadOnlyDictionary<string, string?> Metadata);
