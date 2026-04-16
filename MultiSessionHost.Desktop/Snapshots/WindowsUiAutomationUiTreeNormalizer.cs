using System.Text.Json;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class WindowsUiAutomationUiTreeNormalizer : IUiTreeNormalizer
{
    public UiTree Normalize(UiSnapshotMetadata metadata, JsonElement snapshotRoot) =>
        new(metadata, NormalizeNode(snapshotRoot));

    private static UiNode NormalizeNode(JsonElement element)
    {
        var id = new UiNodeId(GetRequiredString(element, "nodeId"));
        var role = GetRequiredString(element, "role");
        var name = GetOptionalString(element, "name");
        var value = GetOptionalString(element, "value");
        var visible = !GetBoolean(element, "isOffscreen");
        var enabled = GetBoolean(element, "isEnabled");
        var selected = GetNullableBoolean(element, "isSelected") ?? GetBoolean(element, "hasKeyboardFocus");
        var bounds = TryGetBounds(element);
        var attributes = GetAttributes(element);
        var children = GetChildren(element);

        return new UiNode(id, role, name, value ?? name, bounds, visible, enabled, selected, attributes, children);
    }

    private static IReadOnlyList<UiAttribute> GetAttributes(JsonElement element)
    {
        var attributes = new List<UiAttribute>
        {
            new("source", "WindowsUiAutomation"),
            new("identityQuality", GetRequiredString(element, "identityQuality")),
            new("identityBasis", GetRequiredString(element, "identityBasis"))
        };

        AddOptional(attributes, element, "automationId");
        AddOptional(attributes, element, "runtimeId");
        AddOptional(attributes, element, "frameworkId");
        AddOptional(attributes, element, "className");
        AddOptional(attributes, element, "hasKeyboardFocus");

        if (element.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadataElement.EnumerateObject())
            {
                attributes.Add(new UiAttribute($"native.{property.Name}", property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString()));
            }
        }

        return attributes
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

    private static void AddOptional(ICollection<UiAttribute> attributes, JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            attributes.Add(new UiAttribute(propertyName, property.ToString()));
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new InvalidOperationException($"Native UI Automation snapshot node is missing required string property '{propertyName}'.");

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static int GetInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : throw new InvalidOperationException($"Native UI Automation snapshot node is missing required integer property '{propertyName}'.");

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False && property.GetBoolean();

    private static bool? GetNullableBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;
}
