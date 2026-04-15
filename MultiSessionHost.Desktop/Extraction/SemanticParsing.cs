using System.Globalization;
using System.Text.Json;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

internal static class SemanticParsing
{
    public static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static bool IsTrue(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    public static double? GetPercent(UiNode node, IUiTreeQueryService query)
    {
        var direct = ParseDouble(query.GetAttribute(node, "progressPercent")) ??
            ParseDouble(query.GetAttribute(node, "valuePercent")) ??
            ParseDouble(query.GetAttribute(node, "percent"));

        if (direct is not null)
        {
            return NormalizePercent(direct.Value);
        }

        var value = ParseDouble(query.GetAttribute(node, "value"));
        var maximum = ParseDouble(query.GetAttribute(node, "maximum")) ?? ParseDouble(query.GetAttribute(node, "max"));

        if (value is not null && maximum is > 0)
        {
            return Math.Clamp(value.Value / maximum.Value * 100, 0, 100);
        }

        return null;
    }

    public static IReadOnlyList<string> GetJsonStringArrayAttribute(UiNode node, IUiTreeQueryService query, string attributeName)
    {
        var raw = query.GetAttribute(node, attributeName);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? [];
        }
        catch (JsonException)
        {
            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }
    }

    public static string? LabelFor(UiNode node, IUiTreeQueryService query) =>
        query.GatherTextCandidates(node).FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));

    public static bool ContainsAny(string? value, params string[] fragments) =>
        !string.IsNullOrWhiteSpace(value) &&
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static double NormalizePercent(double value) =>
        value <= 1 ? Math.Clamp(value * 100, 0, 100) : Math.Clamp(value, 0, 100);
}
