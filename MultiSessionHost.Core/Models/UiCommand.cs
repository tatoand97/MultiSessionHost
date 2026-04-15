using MultiSessionHost.Core.Enums;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Core.Models;

public sealed record UiCommand
{
    public UiCommand(
        SessionId sessionId,
        UiNodeId? nodeId,
        UiCommandKind kind,
        string? actionName,
        string? textValue,
        bool? boolValue,
        string? selectedValue,
        IReadOnlyDictionary<string, string?>? metadata)
    {
        SessionId = sessionId;
        NodeId = nodeId;
        Kind = kind;
        ActionName = string.IsNullOrWhiteSpace(actionName) ? null : actionName.Trim();
        TextValue = textValue;
        BoolValue = boolValue;
        SelectedValue = string.IsNullOrWhiteSpace(selectedValue) ? null : selectedValue.Trim();
        Metadata = metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
    }

    public SessionId SessionId { get; }

    public UiNodeId? NodeId { get; }

    public UiCommandKind Kind { get; }

    public string? ActionName { get; }

    public string? TextValue { get; }

    public bool? BoolValue { get; }

    public string? SelectedValue { get; }

    public IReadOnlyDictionary<string, string?> Metadata { get; }

    public static UiCommand ClickNode(
        SessionId sessionId,
        UiNodeId nodeId,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.ClickNode, actionName: null, textValue: null, boolValue: null, selectedValue: null, metadata);

    public static UiCommand InvokeNodeAction(
        SessionId sessionId,
        UiNodeId nodeId,
        string? actionName = null,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.InvokeNodeAction, actionName, textValue: null, boolValue: null, selectedValue: null, metadata);

    public static UiCommand SetText(
        SessionId sessionId,
        UiNodeId nodeId,
        string? textValue,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.SetText, actionName: null, textValue, boolValue: null, selectedValue: null, metadata);

    public static UiCommand SelectItem(
        SessionId sessionId,
        UiNodeId nodeId,
        string? selectedValue,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.SelectItem, actionName: null, textValue: null, boolValue: null, selectedValue, metadata);

    public static UiCommand ToggleNode(
        SessionId sessionId,
        UiNodeId nodeId,
        bool? boolValue = null,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.ToggleNode, actionName: null, textValue: null, boolValue, selectedValue: null, metadata);

    public static UiCommand RefreshUi(
        SessionId sessionId,
        UiNodeId? nodeId = null,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(sessionId, nodeId, UiCommandKind.RefreshUi, actionName: null, textValue: null, boolValue: null, selectedValue: null, metadata);
}
