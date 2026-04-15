using System.Text.Json;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Commands;

public sealed class DefaultUiActionResolver : IUiActionResolver
{
    public ResolvedUiAction Resolve(UiTree tree, UiCommand command)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(command);

        if (command.Kind == UiCommandKind.RefreshUi)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.UnsupportedCommand,
                "RefreshUi does not require a node resolver.");
        }

        if (command.NodeId is null)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.InvalidCommandPayload,
                $"UiCommand '{command.Kind}' requires a nodeId.");
        }

        var node = tree.FindById(command.NodeId.Value);

        if (node is null)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.NodeNotFound,
                $"Node '{command.NodeId}' was not found in the current UI tree.");
        }

        EnsureInteractable(node, command.Kind);

        return command.Kind switch
        {
            UiCommandKind.ClickNode => ResolveClick(node, command),
            UiCommandKind.InvokeNodeAction => ResolveInvoke(node, command),
            UiCommandKind.SetText => ResolveSetText(node, command),
            UiCommandKind.ToggleNode => ResolveToggle(node, command),
            UiCommandKind.SelectItem => ResolveSelect(node, command),
            _ => throw new UiCommandFailureException(
                UiCommandFailureCodes.UnsupportedCommand,
                $"UiCommand '{command.Kind}' is not supported by the default resolver.")
        };
    }

    private static ResolvedUiAction ResolveClick(UiNode node, UiCommand command)
    {
        if (!SupportsSemanticAction(node, "click") && !IsButtonLike(node) && !IsClickable(node))
        {
            throw Incompatible(node, command.Kind);
        }

        return CreateResolvedAction(node, command);
    }

    private static ResolvedUiAction ResolveInvoke(UiNode node, UiCommand command)
    {
        var supportedActionNames = GetActionNames(node);
        var actionName = command.ActionName ?? supportedActionNames.FirstOrDefault() ?? node.Text ?? node.Name;

        if (!SupportsSemanticAction(node, "invoke") && supportedActionNames.Count == 0 && !IsButtonLike(node))
        {
            throw Incompatible(node, command.Kind);
        }

        if (!string.IsNullOrWhiteSpace(command.ActionName) &&
            supportedActionNames.Count > 0 &&
            !supportedActionNames.Contains(command.ActionName, StringComparer.OrdinalIgnoreCase))
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.InvalidCommandPayload,
                $"Node '{node.Id}' does not expose action '{command.ActionName}'.");
        }

        return CreateResolvedAction(node, command, actionName: actionName);
    }

    private static ResolvedUiAction ResolveSetText(UiNode node, UiCommand command)
    {
        if (!SupportsSemanticAction(node, "setText") && !IsTextInput(node))
        {
            throw Incompatible(node, command.Kind);
        }

        if (command.TextValue is null)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.InvalidCommandPayload,
                $"UiCommand '{command.Kind}' requires a textValue.");
        }

        return CreateResolvedAction(node, command, textValue: command.TextValue);
    }

    private static ResolvedUiAction ResolveToggle(UiNode node, UiCommand command)
    {
        if (!SupportsSemanticAction(node, "toggle") && !IsToggleLike(node))
        {
            throw Incompatible(node, command.Kind);
        }

        return CreateResolvedAction(node, command, boolValue: command.BoolValue ?? !node.Selected);
    }

    private static ResolvedUiAction ResolveSelect(UiNode node, UiCommand command)
    {
        if (!SupportsSemanticAction(node, "select") && !IsSelectorLike(node))
        {
            throw Incompatible(node, command.Kind);
        }

        if (string.IsNullOrWhiteSpace(command.SelectedValue))
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.InvalidCommandPayload,
                $"UiCommand '{command.Kind}' requires a selectedValue.");
        }

        var availableItems = GetAvailableItems(node);

        if (availableItems.Count > 0 && !availableItems.Contains(command.SelectedValue, StringComparer.Ordinal))
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.InvalidCommandPayload,
                $"Node '{node.Id}' does not contain selectable item '{command.SelectedValue}'.");
        }

        return CreateResolvedAction(node, command, selectedValue: command.SelectedValue);
    }

    private static ResolvedUiAction CreateResolvedAction(
        UiNode node,
        UiCommand command,
        string? actionName = null,
        string? textValue = null,
        bool? boolValue = null,
        string? selectedValue = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var attribute in node.Attributes)
        {
            metadata[attribute.Name] = attribute.Value;
        }

        foreach (var (key, value) in command.Metadata)
        {
            metadata[key] = value;
        }

        return new ResolvedUiAction(
            command.Kind,
            node,
            actionName ?? command.ActionName,
            textValue ?? command.TextValue,
            boolValue ?? command.BoolValue,
            selectedValue ?? command.SelectedValue,
            metadata);
    }

    private static void EnsureInteractable(UiNode node, UiCommandKind kind)
    {
        if (!node.Visible)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.NodeNotVisible,
                $"Node '{node.Id}' is not visible and cannot handle '{kind}'.");
        }

        if (!node.Enabled)
        {
            throw new UiCommandFailureException(
                UiCommandFailureCodes.NodeDisabled,
                $"Node '{node.Id}' is disabled and cannot handle '{kind}'.");
        }
    }

    private static UiCommandFailureException Incompatible(UiNode node, UiCommandKind kind) =>
        new(
            UiCommandFailureCodes.UnsupportedCommand,
            $"UiCommand '{kind}' does not apply to node '{node.Id}' with role '{node.Role}'.");

    private static bool IsButtonLike(UiNode node) =>
        MatchesRole(node, "Button") || MatchesRole(node, "Link") || MatchesRole(node, "Hyperlink");

    private static bool IsTextInput(UiNode node) =>
        MatchesRole(node, "TextBox") ||
        MatchesRole(node, "Input") ||
        MatchesRole(node, "Edit") ||
        AttributeEquals(node, "controlType", "TextBox") ||
        AttributeEquals(node, "acceptsText", "true");

    private static bool IsToggleLike(UiNode node) =>
        MatchesRole(node, "CheckBox") ||
        MatchesRole(node, "ToggleButton") ||
        MatchesRole(node, "Switch") ||
        AttributeEquals(node, "controlType", "CheckBox");

    private static bool IsSelectorLike(UiNode node) =>
        MatchesRole(node, "ListBox") ||
        MatchesRole(node, "List") ||
        MatchesRole(node, "ComboBox") ||
        MatchesRole(node, "Selector") ||
        AttributeEquals(node, "controlType", "ListBox");

    private static bool IsClickable(UiNode node) =>
        AttributeEquals(node, "clickable", "true") ||
        AttributeEquals(node, "invokable", "true") ||
        SupportsSemanticAction(node, "click");

    private static bool MatchesRole(UiNode node, string expectedRole) =>
        string.Equals(node.Role, expectedRole, StringComparison.OrdinalIgnoreCase);

    private static bool AttributeEquals(UiNode node, string name, string expectedValue)
    {
        var value = GetAttributeValue(node, name);
        return string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsSemanticAction(UiNode node, string actionName)
    {
        var value = GetAttributeValue(node, "semanticActions");

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, actionName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetActionNames(UiNode node)
    {
        var explicitActions = GetAttributeValue(node, "actionNames");

        if (!string.IsNullOrWhiteSpace(explicitActions))
        {
            return explicitActions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var command = GetAttributeValue(node, "command");
        return string.IsNullOrWhiteSpace(command) ? [] : [command];
    }

    private static IReadOnlyList<string> GetAvailableItems(UiNode node)
    {
        var serializedItems = GetAttributeValue(node, "items");

        if (string.IsNullOrWhiteSpace(serializedItems))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(serializedItems) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetAttributeValue(UiNode node, string attributeName) =>
        node.Attributes
            .FirstOrDefault(attribute => string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}
