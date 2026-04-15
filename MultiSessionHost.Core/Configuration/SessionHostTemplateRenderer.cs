using System.Text.RegularExpressions;

namespace MultiSessionHost.Core.Configuration;

public static partial class SessionHostTemplateRenderer
{
    public static IReadOnlyCollection<string> GetVariableNames(IEnumerable<string?> templateValues) =>
        templateValues
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(
                value => TemplateVariableRegex()
                    .Matches(value!)
                    .Select(static match => match.Groups["name"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(variables);

        return TemplateVariableRegex().Replace(
            template,
            match =>
            {
                var variableName = match.Groups["name"].Value;

                if (!variables.TryGetValue(variableName, out var value))
                {
                    throw new InvalidOperationException($"The required template variable '{variableName}' was not provided.");
                }

                return value;
            });
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled)]
    private static partial Regex TemplateVariableRegex();
}
