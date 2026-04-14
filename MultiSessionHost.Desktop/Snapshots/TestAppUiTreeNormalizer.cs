using System.Text.Json;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class TestAppUiTreeNormalizer : IUiTreeNormalizer
{
    public UiTree Normalize(UiSnapshotMetadata metadata, JsonElement snapshotRoot) =>
        new(metadata, NormalizeNode(snapshotRoot));

    private static UiNode NormalizeNode(JsonElement element)
    {
        var id = new UiNodeId(GetRequiredString(element, "id"));
        var role = GetRequiredString(element, "role");
        var name = GetOptionalString(element, "name");
        var text = GetOptionalString(element, "text");
        var bounds = TryGetBounds(element);
        var visible = GetBoolean(element, "visible");
        var enabled = GetBoolean(element, "enabled");
        var selected = GetBoolean(element, "selected");
        var attributes = GetAttributes(element);
        var children = GetChildren(element);

        return new UiNode(id, role, name, text, bounds, visible, enabled, selected, attributes, children);
    }

    private static UiBounds? TryGetBounds(JsonElement element)
    {
        if (!element.TryGetProperty("bounds", out var boundsElement) || boundsElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new UiBounds(
            GetInteger(boundsElement, "x"),
            GetInteger(boundsElement, "y"),
            GetInteger(boundsElement, "width"),
            GetInteger(boundsElement, "height"));
    }

    private static IReadOnlyList<UiAttribute> GetAttributes(JsonElement element)
    {
        if (!element.TryGetProperty("attributes", out var attributesElement) || attributesElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return attributesElement
            .EnumerateObject()
            .Select(property => new UiAttribute(property.Name, property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString()))
            .OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<UiNode> GetChildren(JsonElement element)
    {
        if (!element.TryGetProperty("children", out var childrenElement) || childrenElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return childrenElement.EnumerateArray().Select(NormalizeNode).ToArray();
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new InvalidOperationException($"Snapshot node is missing required string property '{propertyName}'.");

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static int GetInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new InvalidOperationException($"Snapshot node is missing required integer property '{propertyName}'.");

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : false;
}
