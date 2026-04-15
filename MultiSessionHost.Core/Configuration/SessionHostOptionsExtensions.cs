using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Configuration;

public static class SessionHostOptionsExtensions
{
    private static readonly string[] ReservedTemplateVariables = ["SessionId"];

    public static IReadOnlyList<SessionDefinition> ToSessionDefinitions(this SessionHostOptions options) =>
        options.Sessions
            .Select(
                session => new SessionDefinition(
                    new SessionId(session.SessionId),
                    session.DisplayName.Trim(),
                    session.Enabled,
                    TimeSpan.FromMilliseconds(session.TickIntervalMs),
                    TimeSpan.FromMilliseconds(session.StartupDelayMs),
                    session.MaxParallelWorkItems,
                    session.MaxRetryCount,
                    TimeSpan.FromMilliseconds(session.InitialBackoffMs),
                    session.Tags
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(static tag => tag.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
            .ToArray();

    public static bool TryValidate(this SessionHostOptions options, out string? error)
    {
        if (options.MaxGlobalParallelSessions <= 0)
        {
            error = "MaxGlobalParallelSessions must be greater than zero.";
            return false;
        }

        if (options.SchedulerIntervalMs <= 0)
        {
            error = "SchedulerIntervalMs must be greater than zero.";
            return false;
        }

        if (options.HealthLogIntervalMs <= 0)
        {
            error = "HealthLogIntervalMs must be greater than zero.";
            return false;
        }

        if (options.EnableAdminApi && !Uri.TryCreate(options.AdminApiUrl, UriKind.Absolute, out _))
        {
            error = "AdminApiUrl must be a valid absolute URL when EnableAdminApi is true.";
            return false;
        }

        if (!Enum.IsDefined(options.DriverMode))
        {
            error = $"DriverMode '{options.DriverMode}' is not valid.";
            return false;
        }

        if (options.EnableUiSnapshots && options.DriverMode != DriverMode.DesktopTargetAdapter)
        {
            error = "EnableUiSnapshots requires DriverMode=DesktopTargetAdapter.";
            return false;
        }

        if (!Enum.IsDefined(options.BindingStorePersistenceMode))
        {
            error = $"BindingStorePersistenceMode '{options.BindingStorePersistenceMode}' is not valid.";
            return false;
        }

        if (options.BindingStorePersistenceMode == BindingStorePersistenceMode.JsonFile &&
            string.IsNullOrWhiteSpace(options.BindingStoreFilePath))
        {
            error = "BindingStoreFilePath is required when BindingStorePersistenceMode=JsonFile.";
            return false;
        }

        if (!TryValidateExecutionCoordination(options.ExecutionCoordination, out error))
        {
            return false;
        }

        if (!TryValidateRiskClassification(options.RiskClassification, out error))
        {
            return false;
        }

        if (options.Sessions.Count == 0)
        {
            error = "At least one session must be configured.";
            return false;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in options.Sessions)
        {
            if (string.IsNullOrWhiteSpace(session.SessionId))
            {
                error = "Each session must have a non-empty SessionId.";
                return false;
            }

            if (!seenIds.Add(session.SessionId.Trim()))
            {
                error = $"SessionId '{session.SessionId}' is duplicated.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(session.DisplayName))
            {
                error = $"Session '{session.SessionId}' must have a DisplayName.";
                return false;
            }

            if (session.TickIntervalMs <= 0)
            {
                error = $"Session '{session.SessionId}' must have TickIntervalMs greater than zero.";
                return false;
            }

            if (session.StartupDelayMs < 0)
            {
                error = $"Session '{session.SessionId}' cannot have a negative StartupDelayMs.";
                return false;
            }

            if (session.MaxParallelWorkItems <= 0)
            {
                error = $"Session '{session.SessionId}' must have MaxParallelWorkItems greater than zero.";
                return false;
            }

            if (session.MaxRetryCount < 0)
            {
                error = $"Session '{session.SessionId}' cannot have a negative MaxRetryCount.";
                return false;
            }

            if (session.InitialBackoffMs <= 0)
            {
                error = $"Session '{session.SessionId}' must have InitialBackoffMs greater than zero.";
                return false;
            }
        }

        var configuredSessionIds = options.Sessions
            .Select(static session => session.SessionId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if ((options.DriverMode == DriverMode.DesktopTargetAdapter || options.DesktopTargets.Count > 0 || options.SessionTargetBindings.Count > 0) &&
            !TryValidateDesktopTargetConfiguration(options, configuredSessionIds, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateDesktopTargetConfiguration(
        SessionHostOptions options,
        IReadOnlySet<string> configuredSessionIds,
        out string? error)
    {
        if (options.DriverMode == DriverMode.DesktopTargetAdapter && options.DesktopTargets.Count == 0)
        {
            error = "DesktopTargets must contain at least one profile when DriverMode=DesktopTargetAdapter.";
            return false;
        }

        if (options.DriverMode == DriverMode.DesktopTargetAdapter && options.SessionTargetBindings.Count == 0)
        {
            error = "SessionTargetBindings must contain at least one binding when DriverMode=DesktopTargetAdapter.";
            return false;
        }

        var profilesByName = new Dictionary<string, DesktopTargetProfileOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in options.DesktopTargets)
        {
            if (string.IsNullOrWhiteSpace(profile.ProfileName))
            {
                error = "Each desktop target profile must have a non-empty ProfileName.";
                return false;
            }

            var profileName = profile.ProfileName.Trim();

            if (!profilesByName.TryAdd(profileName, profile))
            {
                error = $"Desktop target profile '{profile.ProfileName}' is duplicated.";
                return false;
            }

            if (!Enum.IsDefined(profile.Kind))
            {
                error = $"Desktop target profile '{profileName}' has an invalid Kind '{profile.Kind}'.";
                return false;
            }

            if (!Enum.IsDefined(profile.MatchingMode))
            {
                error = $"Desktop target profile '{profileName}' has an invalid MatchingMode '{profile.MatchingMode}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(profile.ProcessName))
            {
                error = $"Desktop target profile '{profileName}' must define ProcessName.";
                return false;
            }

            if (!TryValidateMatchingInputs(
                    profileName,
                    profile.WindowTitleFragment,
                    profile.CommandLineFragmentTemplate,
                    profile.MatchingMode,
                    out error))
            {
                return false;
            }

            if (!RequiresHttpBaseAddress(profile.Kind))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.BaseAddressTemplate))
            {
                error = $"Desktop target profile '{profileName}' must define BaseAddressTemplate.";
                return false;
            }
        }

        var bindingsBySessionId = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in options.SessionTargetBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.SessionId))
            {
                error = "Each session target binding must have a non-empty SessionId.";
                return false;
            }

            var sessionId = binding.SessionId.Trim();

            if (!bindingsBySessionId.Add(sessionId))
            {
                error = $"Session target binding for session '{binding.SessionId}' is duplicated.";
                return false;
            }

            if (!configuredSessionIds.Contains(sessionId))
            {
                error = $"Session target binding '{binding.SessionId}' does not match a configured session.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(binding.TargetProfileName))
            {
                error = $"Session target binding '{sessionId}' must define TargetProfileName.";
                return false;
            }

            var targetProfileName = binding.TargetProfileName.Trim();

            if (!profilesByName.TryGetValue(targetProfileName, out var profile))
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

            var effectiveWindowTitleFragment = binding.Overrides?.WindowTitleFragment ?? profile.WindowTitleFragment;
            var effectiveCommandLineFragment = binding.Overrides?.CommandLineFragmentTemplate ?? profile.CommandLineFragmentTemplate;
            var effectiveMatchingMode = binding.Overrides?.MatchingMode ?? profile.MatchingMode;
            var effectiveBaseAddressTemplate = binding.Overrides?.BaseAddressTemplate ?? profile.BaseAddressTemplate;
            var effectiveProcessName = binding.Overrides?.ProcessName ?? profile.ProcessName;

            if (string.IsNullOrWhiteSpace(effectiveProcessName))
            {
                error = $"Session target binding '{sessionId}' resolved an empty ProcessName.";
                return false;
            }

            if (!TryValidateMatchingInputs(
                    $"binding '{sessionId}'",
                    effectiveWindowTitleFragment,
                    effectiveCommandLineFragment,
                    effectiveMatchingMode,
                    out error))
            {
                return false;
            }

            var requiredVariables = SessionHostTemplateRenderer.GetVariableNames(
                GetTemplatedValues(
                    effectiveProcessName,
                    effectiveWindowTitleFragment,
                    effectiveCommandLineFragment,
                    effectiveBaseAddressTemplate,
                    profile.Metadata.Values,
                    binding.Overrides?.Metadata.Values));
            var availableVariables = BuildValidationVariables(sessionId, binding.Variables);
            var missingVariables = requiredVariables
                .Except(availableVariables.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingVariables.Length > 0)
            {
                error = $"Session target binding '{sessionId}' is missing required template variables: {string.Join(", ", missingVariables)}.";
                return false;
            }

            if (RequiresHttpBaseAddress(profile.Kind))
            {
                if (string.IsNullOrWhiteSpace(effectiveBaseAddressTemplate))
                {
                    error = $"Session target binding '{sessionId}' resolved an empty BaseAddressTemplate.";
                    return false;
                }

                var renderedBaseAddress = SessionHostTemplateRenderer.Render(effectiveBaseAddressTemplate, availableVariables);

                if (!Uri.TryCreate(renderedBaseAddress, UriKind.Absolute, out _))
                {
                    error = $"Session target binding '{sessionId}' rendered an invalid BaseAddressTemplate '{renderedBaseAddress}'.";
                    return false;
                }
            }
        }

        if (options.DriverMode == DriverMode.DesktopTargetAdapter)
        {
            var missingBindings = configuredSessionIds
                .Except(bindingsBySessionId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingBindings.Length > 0)
            {
                error = $"Each configured session requires a SessionTargetBinding when DriverMode=DesktopTargetAdapter. Missing: {string.Join(", ", missingBindings)}.";
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
        IEnumerable<string?> profileMetadataValues,
        IEnumerable<string?>? overrideMetadataValues)
    {
        yield return processName;
        yield return windowTitleFragment;
        yield return commandLineFragmentTemplate;
        yield return baseAddressTemplate;

        foreach (var value in profileMetadataValues)
        {
            yield return value;
        }

        if (overrideMetadataValues is null)
        {
            yield break;
        }

        foreach (var value in overrideMetadataValues)
        {
            yield return value;
        }
    }

    private static bool TryValidateBindingVariables(
        string sessionId,
        SessionTargetBindingOptions binding,
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

    private static IReadOnlyDictionary<string, string> BuildValidationVariables(
        string sessionId,
        IReadOnlyDictionary<string, string?> bindingVariables)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SessionId"] = sessionId
        };

        foreach (var (key, value) in bindingVariables)
        {
            if (value is not null)
            {
                variables[key.Trim()] = value;
            }
        }

        return variables;
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

    private static bool TryValidateExecutionCoordination(
        ExecutionCoordinationOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DefaultTargetCooldownMs < 0)
        {
            error = "ExecutionCoordination.DefaultTargetCooldownMs cannot be negative.";
            return false;
        }

        if (options.MaxConcurrentGlobalTargetOperations <= 0)
        {
            error = "ExecutionCoordination.MaxConcurrentGlobalTargetOperations must be greater than zero.";
            return false;
        }

        if (options.WaitWarningThresholdMs < 0)
        {
            error = "ExecutionCoordination.WaitWarningThresholdMs cannot be negative.";
            return false;
        }

        if (!TryValidateExecutionOperationKinds(options.SessionExclusiveOperationKinds, nameof(ExecutionCoordinationOptions.SessionExclusiveOperationKinds), out error) ||
            !TryValidateExecutionOperationKinds(options.TargetExclusiveOperationKinds, nameof(ExecutionCoordinationOptions.TargetExclusiveOperationKinds), out error) ||
            !TryValidateExecutionOperationKinds(options.GlobalExclusiveOperationKinds, nameof(ExecutionCoordinationOptions.GlobalExclusiveOperationKinds), out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateRiskClassification(
        RiskClassificationOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.DefaultUnknownDisposition))
        {
            error = $"RiskClassification.DefaultUnknownDisposition contains an invalid risk disposition '{options.DefaultUnknownDisposition}'.";
            return false;
        }

        if (!Enum.IsDefined(options.DefaultUnknownSeverity))
        {
            error = $"RiskClassification.DefaultUnknownSeverity contains an invalid risk severity '{options.DefaultUnknownSeverity}'.";
            return false;
        }

        if (!Enum.IsDefined(options.DefaultUnknownPolicy))
        {
            error = $"RiskClassification.DefaultUnknownPolicy contains an invalid risk policy suggestion '{options.DefaultUnknownPolicy}'.";
            return false;
        }

        if (options.MaxReturnedEntities <= 0)
        {
            error = "RiskClassification.MaxReturnedEntities must be greater than zero.";
            return false;
        }

        if (!options.EnableRiskClassification)
        {
            error = null;
            return true;
        }

        if (options.Rules.Count == 0)
        {
            error = "RiskClassification.Rules must contain at least one rule when risk classification is enabled.";
            return false;
        }

        var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.Rules)
        {
            if (!rule.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.RuleName))
            {
                error = "Each enabled risk rule must define RuleName.";
                return false;
            }

            var ruleName = rule.RuleName.Trim();

            if (!ruleNames.Add(ruleName))
            {
                error = $"Risk rule '{rule.RuleName}' is duplicated.";
                return false;
            }

            if (rule.Priority < 0 || rule.Priority > 1000)
            {
                error = $"Risk rule '{ruleName}' must have Priority between 0 and 1000.";
                return false;
            }

            if (!Enum.IsDefined(rule.NameMatchMode))
            {
                error = $"Risk rule '{ruleName}' has an invalid NameMatchMode '{rule.NameMatchMode}'.";
                return false;
            }

            if (!Enum.IsDefined(rule.TypeMatchMode))
            {
                error = $"Risk rule '{ruleName}' has an invalid TypeMatchMode '{rule.TypeMatchMode}'.";
                return false;
            }

            if (!Enum.IsDefined(rule.Disposition))
            {
                error = $"Risk rule '{ruleName}' has an invalid Disposition '{rule.Disposition}'.";
                return false;
            }

            if (!Enum.IsDefined(rule.Severity))
            {
                error = $"Risk rule '{ruleName}' has an invalid Severity '{rule.Severity}'.";
                return false;
            }

            if (!Enum.IsDefined(rule.SuggestedPolicy))
            {
                error = $"Risk rule '{ruleName}' has an invalid SuggestedPolicy '{rule.SuggestedPolicy}'.";
                return false;
            }

            if (rule.MatchByName.Count == 0 && rule.MatchByType.Count == 0 && rule.MatchByTags.Count == 0)
            {
                error = $"Risk rule '{ruleName}' must define at least one name, type, or tag matcher.";
                return false;
            }

            if (rule.MatchByName.Any(static value => string.IsNullOrWhiteSpace(value)) ||
                rule.MatchByType.Any(static value => string.IsNullOrWhiteSpace(value)) ||
                rule.MatchByTags.Any(static value => string.IsNullOrWhiteSpace(value)))
            {
                error = $"Risk rule '{ruleName}' contains an empty matcher value.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidateExecutionOperationKinds(
        IReadOnlyList<ExecutionOperationKind> values,
        string propertyName,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            if (!Enum.IsDefined(value))
            {
                error = $"ExecutionCoordination.{propertyName} contains an invalid execution operation kind '{value}'.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
