using System.Text.Json;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class DefaultUiActionResolverTests
{
    private readonly DefaultUiActionResolver _resolver = new();

    [Fact]
    public void Resolve_ClickNode_WithExistingNode_ReturnsResolvedAction()
    {
        var tree = CreateTree();
        var command = UiCommand.ClickNode(new SessionId("alpha"), new UiNodeId("startButton"));

        var resolved = _resolver.Resolve(tree, command);

        Assert.Equal(UiCommandKind.ClickNode, resolved.Kind);
        Assert.Equal("startButton", resolved.Node.Id.Value);
        Assert.Equal("Button", resolved.Node.Role);
    }

    [Fact]
    public void Resolve_WhenNodeDoesNotExist_Throws()
    {
        var tree = CreateTree();
        var command = UiCommand.ClickNode(new SessionId("alpha"), new UiNodeId("missing-node"));

        var exception = Assert.ThrowsAny<InvalidOperationException>(() => _resolver.Resolve(tree, command));

        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenCommandDoesNotApplyToNode_Throws()
    {
        var tree = CreateTree();
        var command = UiCommand.SetText(new SessionId("alpha"), new UiNodeId("startButton"), "nope");

        var exception = Assert.ThrowsAny<InvalidOperationException>(() => _resolver.Resolve(tree, command));

        Assert.Contains("does not apply", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_SetText_ForTextbox_ReturnsResolvedAction()
    {
        var tree = CreateTree();
        var command = UiCommand.SetText(new SessionId("alpha"), new UiNodeId("notesTextBox"), "updated notes");

        var resolved = _resolver.Resolve(tree, command);

        Assert.Equal(UiCommandKind.SetText, resolved.Kind);
        Assert.Equal("updated notes", resolved.TextValue);
        Assert.Equal("TextBox", resolved.Node.Role);
    }

    [Fact]
    public void Resolve_ToggleNode_ForCheckBox_ReturnsResolvedAction()
    {
        var tree = CreateTree();
        var command = UiCommand.ToggleNode(new SessionId("alpha"), new UiNodeId("enabledCheckBox"), boolValue: false);

        var resolved = _resolver.Resolve(tree, command);

        Assert.Equal(UiCommandKind.ToggleNode, resolved.Kind);
        Assert.False(resolved.BoolValue);
        Assert.Equal("CheckBox", resolved.Node.Role);
    }

    [Fact]
    public void Resolve_SelectItem_ForSelector_ReturnsResolvedAction()
    {
        var tree = CreateTree();
        var command = UiCommand.SelectItem(new SessionId("alpha"), new UiNodeId("itemsListBox"), "alpha-item-2");

        var resolved = _resolver.Resolve(tree, command);

        Assert.Equal(UiCommandKind.SelectItem, resolved.Kind);
        Assert.Equal("alpha-item-2", resolved.SelectedValue);
        Assert.Equal("ListBox", resolved.Node.Role);
    }

    private static UiTree CreateTree()
    {
        var metadata = new UiSnapshotMetadata(
            "alpha",
            "tests",
            DateTimeOffset.UtcNow,
            1,
            2,
            "Alpha Window",
            new Dictionary<string, string?>());

        var root = new UiNode(
            new UiNodeId("root"),
            "Form",
            "root",
            "Root",
            Bounds: null,
            Visible: true,
            Enabled: true,
            Selected: false,
            Attributes: [],
            Children:
            [
                new UiNode(
                    new UiNodeId("startButton"),
                    "Button",
                    "startButton",
                    "Start",
                    Bounds: null,
                    Visible: true,
                    Enabled: true,
                    Selected: false,
                    Attributes:
                    [
                        new UiAttribute("controlType", "Button"),
                        new UiAttribute("command", "Start"),
                        new UiAttribute("actionNames", "Start,startButton"),
                        new UiAttribute("semanticActions", "click,invoke"),
                        new UiAttribute("clickable", "true"),
                        new UiAttribute("invokable", "true")
                    ],
                    Children: []),
                new UiNode(
                    new UiNodeId("notesTextBox"),
                    "TextBox",
                    "notesTextBox",
                    "Notes",
                    Bounds: null,
                    Visible: true,
                    Enabled: true,
                    Selected: false,
                    Attributes:
                    [
                        new UiAttribute("controlType", "TextBox"),
                        new UiAttribute("acceptsText", "true"),
                        new UiAttribute("semanticActions", "setText")
                    ],
                    Children: []),
                new UiNode(
                    new UiNodeId("enabledCheckBox"),
                    "CheckBox",
                    "enabledCheckBox",
                    "Enabled",
                    Bounds: null,
                    Visible: true,
                    Enabled: true,
                    Selected: true,
                    Attributes:
                    [
                        new UiAttribute("controlType", "CheckBox"),
                        new UiAttribute("checked", "true"),
                        new UiAttribute("semanticActions", "click,toggle")
                    ],
                    Children: []),
                new UiNode(
                    new UiNodeId("itemsListBox"),
                    "ListBox",
                    "itemsListBox",
                    string.Empty,
                    Bounds: null,
                    Visible: true,
                    Enabled: true,
                    Selected: true,
                    Attributes:
                    [
                        new UiAttribute("controlType", "ListBox"),
                        new UiAttribute("semanticActions", "select"),
                        new UiAttribute("items", JsonSerializer.Serialize(new[] { "alpha-item-1", "alpha-item-2", "alpha-item-3" }))
                    ],
                    Children: [])
            ]);

        return new UiTree(metadata, root);
    }
}
