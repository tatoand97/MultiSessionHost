using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class DesktopTargetProfileResolution
{
    public static DesktopTargetProfile ApplyOverrides(DesktopTargetProfile profile, DesktopTargetProfileOverride? overrides)
    {
        if (overrides is null)
        {
            return profile;
        }

        var metadata = profile.Metadata
            .Concat(overrides.Metadata)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

        return profile with
        {
            ProcessName = overrides.ProcessName ?? profile.ProcessName,
            WindowTitleFragment = overrides.WindowTitleFragment ?? profile.WindowTitleFragment,
            CommandLineFragmentTemplate = overrides.CommandLineFragmentTemplate ?? profile.CommandLineFragmentTemplate,
            BaseAddressTemplate = overrides.BaseAddressTemplate ?? profile.BaseAddressTemplate,
            MatchingMode = overrides.MatchingMode ?? profile.MatchingMode,
            Metadata = metadata,
            SupportsUiSnapshots = overrides.SupportsUiSnapshots ?? profile.SupportsUiSnapshots,
            SupportsStateEndpoint = overrides.SupportsStateEndpoint ?? profile.SupportsStateEndpoint
        };
    }

    public static IReadOnlyDictionary<string, string> BuildVariables(
        SessionId sessionId,
        IReadOnlyDictionary<string, string> bindingVariables)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionId"] = sessionId.Value
        };

        foreach (var (key, value) in bindingVariables)
        {
            variables[key] = value;
        }

        return variables;
    }

    public static string RenderRequired(string template, IReadOnlyDictionary<string, string> variables, string fieldName)
    {
        var rendered = SessionHostTemplateRenderer.Render(template, variables).Trim();
        return rendered.Length == 0
            ? throw new InvalidOperationException($"The rendered {fieldName} is empty.")
            : rendered;
    }

    public static string? RenderOptional(string? template, IReadOnlyDictionary<string, string> variables) =>
        string.IsNullOrWhiteSpace(template)
            ? null
            : SessionHostTemplateRenderer.Render(template, variables).Trim();

    public static Uri? RenderUri(string? template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var rendered = SessionHostTemplateRenderer.Render(template, variables);
        return Uri.TryCreate(rendered, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"The rendered BaseAddressTemplate '{rendered}' is not a valid absolute URI.");
    }

    public static IReadOnlyDictionary<string, string?> RenderMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyDictionary<string, string> variables) =>
        metadata.ToDictionary(
            static pair => pair.Key,
            pair => string.IsNullOrWhiteSpace(pair.Value) ? pair.Value : SessionHostTemplateRenderer.Render(pair.Value, variables),
            StringComparer.OrdinalIgnoreCase);
}
