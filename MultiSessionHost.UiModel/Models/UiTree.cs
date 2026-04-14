namespace MultiSessionHost.UiModel.Models;

public sealed record UiTree(
    UiSnapshotMetadata Metadata,
    UiNode Root);
