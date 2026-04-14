using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IUiSnapshotSerializer
{
    string Serialize(UiSnapshotEnvelope envelope);

    UiSnapshotEnvelope Deserialize(string json);
}
