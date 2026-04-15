using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class SessionTargetBindingValidation
{
    private static readonly string[] ReservedTemplateVariables = ["SessionId"];

    public static bool TryValidate(
        SessionTargetBinding binding,
        IReadOnlySet<string> configuredSessionIds,
        IDesktopTargetProfileCatalog profileCatalog,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(profileCatalog);

        var sessionId = binding.SessionId.Value;

        if (!configuredSessionIds.Contains(sessionId))
        {
            error = $"Session target binding '{sessionId}' does not match a configured session.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.TargetProfileName))
        {
            error = $"Session target binding '{sessionId}' must define TargetProfileName.";
            return false;
        }

        var profile = profileCatalog.TryGetProfile(binding.TargetProfileName);

        if (profile is null)
        {
            error = $"Session target binding '{sessionId}' references unknown profile '{binding.TargetProfileName}'.";
            return false;
        }

        if (!TryValidateBindingVariables(sessionId, binding, out error))
        {
            return false;
        }

        if (binding.Overrides?.MatchingMode is { } matchingMode && !Enum.IsDefined(matchingMode))
        {
            error = $"Session target binding '{sessionId}' has an invalid override MatchingMode '{matchingMode}'.";
            return false;
        }

        var effectiveProfile = DesktopTargetProfileResolution.ApplyOverrides(profile, binding.Overrides);

        if (string.IsNullOrWhiteSpace(effectiveProfile.ProcessName))
        {
            error = $"Session target binding '{sessionId}' resolved an empty ProcessName.";
            return false;
        }

        if (!TryValidateMatchingInputs(
                $"binding '{sessionId}'",
                effectiveProfile.WindowTitleFragment,
                effectiveProfile.CommandLineFragmentTemplate,
                effectiveProfile.MatchingMode,
                out error))
        {
            return false;
        }

        var variables = DesktopTargetProfileResolution.BuildVariables(binding.SessionId, binding.Variables);
        var requiredVariables = SessionHostTemplateRenderer.GetVariableNames(
            GetTemplatedValues(
                effectiveProfile.ProcessName,
                effectiveProfile.WindowTitleFragment,
                effectiveProfile.CommandLineFragmentTemplate,
                effectiveProfile.BaseAddressTemplate,
                effectiveProfile.Metadata.Values));
        var missingVariables = requiredVariables
            .Except(variables.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingVariables.Length > 0)
        {
            error = $"Session target binding '{sessionId}' is missing required template variables: {string.Join(", ", missingVariables)}.";
            return false;
        }

        if (RequiresHttpBaseAddress(effectiveProfile.Kind))
        {
            if (string.IsNullOrWhiteSpace(effectiveProfile.BaseAddressTemplate))
            {
                error = $"Session target binding '{sessionId}' resolved an empty BaseAddressTemplate.";
                return false;
            }

            var renderedBaseAddress = SessionHostTemplateRenderer.Render(effectiveProfile.BaseAddressTemplate, variables);

            if (!Uri.TryCreate(renderedBaseAddress, UriKind.Absolute, out _))
            {
                error = $"Session target binding '{sessionId}' rendered an invalid BaseAddressTemplate '{renderedBaseAddress}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static IEnumerable<string?> GetTemplatedValues(
        string? processName,
        string? windowTitleFragment,
        string? commandLineFragmentTemplate,
        string? baseAddressTemplate,
        IEnumerable<string?> metadataValues)
    {
        yield return processName;
        yield return windowTitleFragment;
        yield return commandLineFragmentTemplate;
        yield return baseAddressTemplate;

        foreach (var value in metadataValues)
        {
            yield return value;
        }
    }

    private static bool TryValidateBindingVariables(
        string sessionId,
        SessionTargetBinding binding,
        out string? error)
    {
        foreach (var (key, value) in binding.Variables)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                error = $"Session target binding '{sessionId}' contains a variable with an empty key.";
                return false;
            }

            if (ReservedTemplateVariables.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Session target binding '{sessionId}' cannot override reserved variable '{key}'.";
                return false;
            }

            if (value is null)
            {
                error = $"Session target binding '{sessionId}' contains a null value for variable '{key}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidateMatchingInputs(
        string scope,
        string? windowTitleFragment,
        string? commandLineFragmentTemplate,
        DesktopSessionMatchingMode matchingMode,
        out string? error)
    {
        switch (matchingMode)
        {
            case DesktopSessionMatchingMode.WindowTitle when string.IsNullOrWhiteSpace(windowTitleFragment):
                error = $"{scope} requires WindowTitleFragment when MatchingMode=WindowTitle.";
                return false;

            case DesktopSessionMatchingMode.CommandLine when string.IsNullOrWhiteSpace(commandLineFragmentTemplate):
                error = $"{scope} requires CommandLineFragmentTemplate when MatchingMode=CommandLine.";
                return false;

            case DesktopSessionMatchingMode.WindowTitleAndCommandLine
                when string.IsNullOrWhiteSpace(windowTitleFragment) || string.IsNullOrWhiteSpace(commandLineFragmentTemplate):
                error = $"{scope} requires both WindowTitleFragment and CommandLineFragmentTemplate when MatchingMode=WindowTitleAndCommandLine.";
                return false;

            default:
                error = null;
                return true;
        }
    }

    private static bool RequiresHttpBaseAddress(DesktopTargetKind kind) =>
        kind is DesktopTargetKind.SelfHostedHttpDesktop or DesktopTargetKind.DesktopTestApp;
}
