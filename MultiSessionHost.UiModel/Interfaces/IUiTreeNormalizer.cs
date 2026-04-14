using System.Text.Json;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.UiModel.Interfaces;

public interface IUiTreeNormalizer
{
    UiTree Normalize(UiSnapshotMetadata metadata, JsonElement snapshotRoot);
}
