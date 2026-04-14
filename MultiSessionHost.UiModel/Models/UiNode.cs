namespace MultiSessionHost.UiModel.Models;

public sealed record UiNode(
    UiNodeId Id,
    string Role,
    string? Name,
    string? Text,
    UiBounds? Bounds,
    bool Visible,
    bool Enabled,
    bool Selected,
    IReadOnlyList<UiAttribute> Attributes,
    IReadOnlyList<UiNode> Children);
