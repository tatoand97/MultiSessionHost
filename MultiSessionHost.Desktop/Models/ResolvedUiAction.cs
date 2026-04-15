using MultiSessionHost.Core.Enums;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Models;

public sealed record ResolvedUiAction(
    UiCommandKind Kind,
    UiNode Node,
    string? ActionName,
    string? TextValue,
    bool? BoolValue,
    string? SelectedValue,
    IReadOnlyDictionary<string, string?> Metadata);
