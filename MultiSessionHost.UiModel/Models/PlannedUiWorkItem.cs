namespace MultiSessionHost.UiModel.Models;

public sealed record PlannedUiWorkItem(
    string Kind,
    string Description,
    string? TargetNodeId);
