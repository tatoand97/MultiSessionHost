using System.Text.Json;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class JsonUiSnapshotSerializer : IUiSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize(UiSnapshotEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, Options);

    public UiSnapshotEnvelope Deserialize(string json) =>
        JsonSerializer.Deserialize<UiSnapshotEnvelope>(json, Options)
        ?? throw new InvalidOperationException("The UI snapshot payload could not be deserialized.");
}
