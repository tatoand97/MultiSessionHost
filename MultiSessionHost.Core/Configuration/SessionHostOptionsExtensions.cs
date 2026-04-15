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

        if (!TryValidatePolicyEngine(options.PolicyEngine, out error))
        {
            return false;
        }

        if (!TryValidateDecisionExecution(options.DecisionExecution, out error))
        {
            return false;
        }

        if (!TryValidateOperationalMemory(options.OperationalMemory, out error))
        {
            return false;
        }

        if (!TryValidateRuntimePersistence(options.RuntimePersistence, out error))
        {
            return false;
        }

        if (!TryValidatePolicyControl(options.PolicyControl, out error))
        {
            return false;
        }

        if (!TryValidateObservability(options.Observability, out error))
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

    private static bool TryValidateOperationalMemory(
        OperationalMemoryOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxHistoryEntries <= 0)
        {
            error = "OperationalMemory.MaxHistoryEntries must be greater than zero.";
            return false;
        }

        if (options.MaxWorksitesPerSession <= 0)
        {
            error = "OperationalMemory.MaxWorksitesPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxRiskObservationsPerSession <= 0)
        {
            error = "OperationalMemory.MaxRiskObservationsPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxPresenceObservationsPerSession <= 0)
        {
            error = "OperationalMemory.MaxPresenceObservationsPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxTimingObservationsPerSession <= 0)
        {
            error = "OperationalMemory.MaxTimingObservationsPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxOutcomeObservationsPerSession <= 0)
        {
            error = "OperationalMemory.MaxOutcomeObservationsPerSession must be greater than zero.";
            return false;
        }

        if (options.StaleAfterMinutes < 0)
        {
            error = "OperationalMemory.StaleAfterMinutes cannot be negative.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateRuntimePersistence(
        RuntimePersistenceOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.Mode))
        {
            error = $"RuntimePersistence.Mode '{options.Mode}' is not valid.";
            return false;
        }

        if (!options.EnableRuntimePersistence)
        {
            error = null;
            return true;
        }

        if (options.Mode == RuntimePersistenceMode.None)
        {
            error = "RuntimePersistence.Mode cannot be None when RuntimePersistence.EnableRuntimePersistence=true.";
            return false;
        }

        if (options.Mode == RuntimePersistenceMode.JsonFile && string.IsNullOrWhiteSpace(options.BasePath))
        {
            error = "RuntimePersistence.BasePath is required when RuntimePersistence.Mode=JsonFile.";
            return false;
        }

        if (options.SchemaVersion <= 0)
        {
            error = "RuntimePersistence.SchemaVersion must be greater than zero.";
            return false;
        }

        if (options.MaxDecisionHistoryEntries <= 0)
        {
            error = "RuntimePersistence.MaxDecisionHistoryEntries must be greater than zero.";
            return false;
        }

        if (options.MaxPersistedSessions < 0)
        {
            error = "RuntimePersistence.MaxPersistedSessions cannot be negative.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidatePolicyControl(
        PolicyControlOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxHistoryEntries <= 0)
        {
            error = "PolicyControl.MaxHistoryEntries must be greater than zero.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateObservability(
        ObservabilityOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxEventsPerSession <= 0)
        {
            error = "Observability.MaxEventsPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxErrorsPerSession <= 0)
        {
            error = "Observability.MaxErrorsPerSession must be greater than zero.";
            return false;
        }

        if (options.MaxReasonMetricsPerSession <= 0)
        {
            error = "Observability.MaxReasonMetricsPerSession must be greater than zero.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateDecisionExecution(
        DecisionExecutionOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxHistoryEntries <= 0)
        {
            error = "DecisionExecution.MaxHistoryEntries must be greater than zero.";
            return false;
        }

        if (options.RepeatSuppressionWindowMs < 0)
        {
            error = "DecisionExecution.RepeatSuppressionWindowMs cannot be negative.";
            return false;
        }

        if (options.AutoExecuteAfterEvaluation && !options.EnableDecisionExecution)
        {
            error = "DecisionExecution.AutoExecuteAfterEvaluation requires DecisionExecution.EnableDecisionExecution=true.";
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

    private static bool TryValidatePolicyEngine(
        PolicyEngineOptions options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxReturnedDirectives <= 0)
        {
            error = "PolicyEngine.MaxReturnedDirectives must be greater than zero.";
            return false;
        }

        if (options.MinDirectivePriority < 0)
        {
            error = "PolicyEngine.MinDirectivePriority cannot be negative.";
            return false;
        }

        var validPolicyNames = new HashSet<string>(
            [
                "AbortPolicy",
                "ThreatResponsePolicy",
                "TransitPolicy",
                "ResourceUsagePolicy",
                "TargetPrioritizationPolicy",
                "SelectNextSitePolicy"
            ],
            StringComparer.OrdinalIgnoreCase);

        if (options.PolicyOrder.Count == 0)
        {
            error = "PolicyEngine.PolicyOrder must contain at least one policy.";
            return false;
        }

        var seenPolicyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var policyName in options.PolicyOrder)
        {
            if (string.IsNullOrWhiteSpace(policyName))
            {
                error = "PolicyEngine.PolicyOrder contains an empty policy name.";
                return false;
            }

            var trimmedPolicyName = policyName.Trim();

            if (!validPolicyNames.Contains(trimmedPolicyName))
            {
                error = $"PolicyEngine.PolicyOrder contains unknown policy '{policyName}'.";
                return false;
            }

            if (!seenPolicyNames.Add(trimmedPolicyName))
            {
                error = $"PolicyEngine.PolicyOrder contains duplicate policy '{policyName}'.";
                return false;
            }
        }

        if (!TryValidatePriority(options.AbortPolicy.AbortPriority, nameof(options.AbortPolicy.AbortPriority), out error) ||
            !TryValidatePriority(options.AbortPolicy.PausePriority, nameof(options.AbortPolicy.PausePriority), out error) ||
            !TryValidatePriority(options.ThreatResponsePolicy.WithdrawPriority, nameof(options.ThreatResponsePolicy.WithdrawPriority), out error) ||
            !TryValidatePriority(options.ThreatResponsePolicy.PausePriority, nameof(options.ThreatResponsePolicy.PausePriority), out error) ||
            !TryValidatePriority(options.ThreatResponsePolicy.PrioritizePriority, nameof(options.ThreatResponsePolicy.PrioritizePriority), out error) ||
            !TryValidatePriority(options.ThreatResponsePolicy.AvoidPriority, nameof(options.ThreatResponsePolicy.AvoidPriority), out error) ||
            !TryValidatePriority(options.ThreatResponsePolicy.ObservePriority, nameof(options.ThreatResponsePolicy.ObservePriority), out error) ||
            !TryValidatePriority(options.TransitPolicy.WaitPriority, nameof(options.TransitPolicy.WaitPriority), out error) ||
            !TryValidatePriority(options.TransitPolicy.NavigatePriority, nameof(options.TransitPolicy.NavigatePriority), out error) ||
            !TryValidatePriority(options.TransitPolicy.BlockedPriority, nameof(options.TransitPolicy.BlockedPriority), out error) ||
            !TryValidatePriority(options.ResourceUsagePolicy.CriticalPriority, nameof(options.ResourceUsagePolicy.CriticalPriority), out error) ||
            !TryValidatePriority(options.ResourceUsagePolicy.DegradedPriority, nameof(options.ResourceUsagePolicy.DegradedPriority), out error) ||
            !TryValidatePriority(options.TargetPrioritizationPolicy.PrioritizePriority, nameof(options.TargetPrioritizationPolicy.PrioritizePriority), out error) ||
            !TryValidatePriority(options.TargetPrioritizationPolicy.SelectPriority, nameof(options.TargetPrioritizationPolicy.SelectPriority), out error) ||
            !TryValidatePriority(options.TargetPrioritizationPolicy.AvoidPriority, nameof(options.TargetPrioritizationPolicy.AvoidPriority), out error) ||
            !TryValidatePriority(options.SelectNextSitePolicy.SelectSitePriority, nameof(options.SelectNextSitePolicy.SelectSitePriority), out error) ||
            !TryValidatePriority(options.SelectNextSitePolicy.ObservePriority, nameof(options.SelectNextSitePolicy.ObservePriority), out error))
        {
            return false;
        }

        if (options.ResourceUsagePolicy.CriticalPercentThreshold < 0 ||
            options.ResourceUsagePolicy.CriticalPercentThreshold > 100 ||
            options.ResourceUsagePolicy.DegradedPercentThreshold < 0 ||
            options.ResourceUsagePolicy.DegradedPercentThreshold > 100)
        {
            error = "PolicyEngine.ResourceUsagePolicy thresholds must be between 0 and 100.";
            return false;
        }

        if (options.ResourceUsagePolicy.CriticalPercentThreshold > options.ResourceUsagePolicy.DegradedPercentThreshold)
        {
            error = "PolicyEngine.ResourceUsagePolicy.CriticalPercentThreshold cannot be greater than DegradedPercentThreshold.";
            return false;
        }

        if (!TryValidateAggregationRules(options.AggregationRules, out error))
        {
            return false;
        }

        if (!TryValidatePolicyRuleFamilies(options.Rules, "PolicyEngine.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.SelectNextSitePolicy.Rules, "PolicyEngine.SelectNextSitePolicy.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.ThreatResponsePolicy.Rules, "PolicyEngine.ThreatResponsePolicy.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.TargetPrioritizationPolicy.Rules, "PolicyEngine.TargetPrioritizationPolicy.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.ResourceUsagePolicy.Rules, "PolicyEngine.ResourceUsagePolicy.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.TransitPolicy.Rules, "PolicyEngine.TransitPolicy.Rules", out error) ||
            !TryValidatePolicyRuleFamilies(options.AbortPolicy.Rules, "PolicyEngine.AbortPolicy.Rules", out error))
        {
            return false;
        }

        if (!TryValidateMemoryDecisioning(options.MemoryDecisioning, out error))
        {
            return false;
        }

        if (!IsValidDirectiveKind(options.Rules.SiteSelection.NoAllowedCandidateDirectiveKind))
        {
            error = "PolicyEngine.Rules.SiteSelection.NoAllowedCandidateDirectiveKind is not a valid directive kind.";
            return false;
        }

        if (!TryValidatePriority(options.Rules.SiteSelection.NoAllowedCandidatePriority, "Rules.SiteSelection.NoAllowedCandidatePriority", out error))
        {
            return false;
        }

        if (options.Rules.SiteSelection.MinimumWaitMs < 0)
        {
            error = "PolicyEngine.Rules.SiteSelection.MinimumWaitMs cannot be negative.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateMemoryDecisioning(MemoryDecisioningOptions options, out string? error)
    {
        if (options.SiteSelection.MinimumSuccessfulVisits < 0)
        {
            error = "PolicyEngine.MemoryDecisioning.SiteSelection.MinimumSuccessfulVisits cannot be negative.";
            return false;
        }

        if (!IsFraction(options.SiteSelection.FailurePenaltyWeight) ||
            !IsFraction(options.SiteSelection.OccupancyPenaltyWeight) ||
            !IsFraction(options.SiteSelection.SuccessBoostWeight))
        {
            error = "PolicyEngine.MemoryDecisioning.SiteSelection weight values must be between 0 and 1.";
            return false;
        }

        if (options.SiteSelection.RecentFailureWindowMinutes < 0 || options.SiteSelection.RecentOccupancyWindowMinutes < 0)
        {
            error = "PolicyEngine.MemoryDecisioning.SiteSelection recent windows cannot be negative.";
            return false;
        }

        if (!IsKnownStaleMode(options.SiteSelection.StaleMemoryPenaltyMode))
        {
            error = "PolicyEngine.MemoryDecisioning.SiteSelection.StaleMemoryPenaltyMode must be Ignore, SoftPenalty, or StrictPenalty.";
            return false;
        }

        if (!IsKnownRiskSeverity(options.SiteSelection.AvoidWorksitesAboveRememberedRiskSeverity) ||
            !IsKnownRiskSeverity(options.ThreatResponse.AvoidRiskSeverityThreshold))
        {
            error = "PolicyEngine.MemoryDecisioning risk severity thresholds must be Critical, High, Moderate, Low, or Unknown.";
            return false;
        }

        if (options.ThreatResponse.RepeatedHighRiskThreshold < 1)
        {
            error = "PolicyEngine.MemoryDecisioning.ThreatResponse.RepeatedHighRiskThreshold must be at least 1.";
            return false;
        }

        if (options.Transit.LongWaitThresholdMs < 0 || options.Transit.MaxRememberedWaitBeforeMoveOnMs < 0)
        {
            error = "PolicyEngine.MemoryDecisioning.Transit thresholds cannot be negative.";
            return false;
        }

        if (options.Transit.MaxRememberedWaitBeforeMoveOnMs < options.Transit.LongWaitThresholdMs)
        {
            error = "PolicyEngine.MemoryDecisioning.Transit.MaxRememberedWaitBeforeMoveOnMs cannot be less than LongWaitThresholdMs.";
            return false;
        }

        if (options.Abort.RepeatedFailureThreshold < 1)
        {
            error = "PolicyEngine.MemoryDecisioning.Abort.RepeatedFailureThreshold must be at least 1.";
            return false;
        }

        if (options.Abort.FailureWindowMinutes < 0)
        {
            error = "PolicyEngine.MemoryDecisioning.Abort.FailureWindowMinutes cannot be negative.";
            return false;
        }

        if (options.Abort.MemoryReinforceAbortPriorityBoost < 0)
        {
            error = "PolicyEngine.MemoryDecisioning.Abort.MemoryReinforceAbortPriorityBoost cannot be negative.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsFraction(double value) => value >= 0 && value <= 1;

    private static bool IsKnownStaleMode(string? value) =>
        string.Equals(value, "Ignore", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "SoftPenalty", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "StrictPenalty", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownRiskSeverity(string? value) =>
        string.Equals(value, "Critical", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "High", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Moderate", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Low", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static bool TryValidatePolicyRuleFamilies(BehaviorRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleFamilies(rules.SiteSelection, $"{path}.SiteSelection", out error) &&
        TryValidatePolicyRuleFamilies(rules.ThreatResponse, $"{path}.ThreatResponse", out error) &&
        TryValidatePolicyRuleFamilies(rules.TargetPrioritization, $"{path}.TargetPrioritization", out error) &&
        TryValidatePolicyRuleFamilies(rules.ResourceUsage, $"{path}.ResourceUsage", out error) &&
        TryValidatePolicyRuleFamilies(rules.Transit, $"{path}.Transit", out error) &&
        TryValidatePolicyRuleFamilies(rules.Abort, $"{path}.Abort", out error);

    private static bool TryValidatePolicyRuleFamilies(SiteSelectionRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.AllowRules", rules.AllowRules, requireMatcher: false, AllowedSiteSelectionDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedSiteSelectionDirectiveKinds(), out error);

    private static bool TryValidatePolicyRuleFamilies(ThreatResponseRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.RetreatRules", rules.RetreatRules, requireMatcher: true, AllowedThreatResponseDirectiveKinds(), out error) &&
        TryValidatePolicyRuleSet($"{path}.DenyRules", rules.DenyRules, requireMatcher: true, AllowedThreatResponseDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedThreatResponseDirectiveKinds(), out error);

    private static bool TryValidatePolicyRuleFamilies(TargetPrioritizationRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.PriorityRules", rules.PriorityRules, requireMatcher: true, AllowedTargetPrioritizationDirectiveKinds(), out error) &&
        TryValidatePolicyRuleSet($"{path}.DenyRules", rules.DenyRules, requireMatcher: true, AllowedTargetPrioritizationDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedTargetPrioritizationDirectiveKinds(), out error);

    private static bool TryValidatePolicyRuleFamilies(ResourceUsageRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.Rules", rules.Rules, requireMatcher: true, AllowedResourceUsageDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedResourceUsageDirectiveKinds(), out error);

    private static bool TryValidatePolicyRuleFamilies(TransitRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.Rules", rules.Rules, requireMatcher: true, AllowedTransitDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedTransitDirectiveKinds(), out error);

    private static bool TryValidatePolicyRuleFamilies(AbortRulesOptions rules, string path, out string? error) =>
        TryValidatePolicyRuleSet($"{path}.Rules", rules.Rules, requireMatcher: true, AllowedAbortDirectiveKinds(), out error) &&
        TryValidateFallbackRule($"{path}.Fallback", rules.Fallback, AllowedAbortDirectiveKinds(), out error);

    private static bool TryValidateAggregationRules(
        DecisionPlanAggregationRulesOptions options,
        out string? error)
    {
        var suppressionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.SuppressionRules.Where(static rule => rule.Enabled))
        {
            if (string.IsNullOrWhiteSpace(rule.RuleName))
            {
                error = "PolicyEngine.AggregationRules.SuppressionRules contains an enabled rule without RuleName.";
                return false;
            }

            var ruleName = rule.RuleName.Trim();

            if (!suppressionNames.Add(ruleName))
            {
                error = $"PolicyEngine.AggregationRules.SuppressionRules contains duplicate rule '{rule.RuleName}'.";
                return false;
            }

            if (rule.TriggerDirectiveKinds.Count == 0)
            {
                error = $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName} must define TriggerDirectiveKinds.";
                return false;
            }

            if (rule.SuppressedDirectiveKinds.Count == 0)
            {
                error = $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName} must define SuppressedDirectiveKinds.";
                return false;
            }

            if (!TryValidateDirectiveKindList(rule.TriggerDirectiveKinds, allowWildcard: false, $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName}.TriggerDirectiveKinds", out error) ||
                !TryValidateDirectiveKindList(rule.SuppressedDirectiveKinds, allowWildcard: true, $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName}.SuppressedDirectiveKinds", out error) ||
                !TryValidateDirectiveKindList(rule.PreserveDirectiveKinds, allowWildcard: false, $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName}.PreserveDirectiveKinds", out error) ||
                !TryValidateDirectiveKindList(rule.BlockedByDirectiveKinds, allowWildcard: false, $"PolicyEngine.AggregationRules.SuppressionRules.{ruleName}.BlockedByDirectiveKinds", out error))
            {
                return false;
            }
        }

        var statusNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.StatusRules.Where(static rule => rule.Enabled))
        {
            if (string.IsNullOrWhiteSpace(rule.RuleName))
            {
                error = "PolicyEngine.AggregationRules.StatusRules contains an enabled rule without RuleName.";
                return false;
            }

            var ruleName = rule.RuleName.Trim();

            if (!statusNames.Add(ruleName))
            {
                error = $"PolicyEngine.AggregationRules.StatusRules contains duplicate rule '{rule.RuleName}'.";
                return false;
            }

            if (!IsValidPlanStatus(rule.Status))
            {
                error = $"PolicyEngine.AggregationRules.StatusRules.{ruleName}.Status '{rule.Status}' is not valid.";
                return false;
            }

            if (rule.DirectiveKinds.Count == 0 && !rule.IncludePolicyAbortFlag)
            {
                error = $"PolicyEngine.AggregationRules.StatusRules.{ruleName} must define DirectiveKinds or IncludePolicyAbortFlag.";
                return false;
            }

            if (!TryValidateDirectiveKindList(rule.DirectiveKinds, allowWildcard: false, $"PolicyEngine.AggregationRules.StatusRules.{ruleName}.DirectiveKinds", out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidatePolicyRuleSet(
        string path,
        IReadOnlyList<PolicyRuleOptions> rules,
        bool requireMatcher,
        IReadOnlySet<string> allowedDirectiveKinds,
        out string? error)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules.Where(static rule => rule.Enabled))
        {
            if (string.IsNullOrWhiteSpace(rule.RuleName))
            {
                error = $"{path} contains an enabled rule without RuleName.";
                return false;
            }

            var ruleName = rule.RuleName.Trim();

            if (!names.Add(ruleName))
            {
                error = $"{path} contains duplicate rule '{rule.RuleName}'.";
                return false;
            }

            if (!TryValidatePriority(rule.Priority, $"{path}.{ruleName}.Priority", out error))
            {
                return false;
            }

            if (rule.MinimumWaitMs < 0)
            {
                error = $"{path}.{ruleName}.MinimumWaitMs cannot be negative.";
                return false;
            }

            if (!Enum.IsDefined(rule.LabelMatchMode))
            {
                error = $"{path}.{ruleName}.LabelMatchMode is not valid.";
                return false;
            }

            if (!Enum.IsDefined(rule.TypeMatchMode))
            {
                error = $"{path}.{ruleName}.TypeMatchMode is not valid.";
                return false;
            }

            if (!IsValidDirectiveKind(rule.DirectiveKind))
            {
                error = $"{path}.{ruleName}.DirectiveKind '{rule.DirectiveKind}' is not valid.";
                return false;
            }

            if (!allowedDirectiveKinds.Contains(rule.DirectiveKind.Trim()))
            {
                error = $"{path}.{ruleName}.DirectiveKind '{rule.DirectiveKind}' is not allowed for this policy rule family.";
                return false;
            }

            if (rule.MatchLabels.Any(static value => string.IsNullOrWhiteSpace(value)) ||
                rule.MatchTypes.Any(static value => string.IsNullOrWhiteSpace(value)) ||
                rule.MatchTags.Any(static value => string.IsNullOrWhiteSpace(value)))
            {
                error = $"{path}.{ruleName} contains an empty matcher value.";
                return false;
            }

            if (!TryValidateRange(rule.MinProgressPercent, rule.MaxProgressPercent, 0, 100, $"{path}.{ruleName}.ProgressPercent", out error) ||
                !TryValidateRange(rule.MinResourcePercent, rule.MaxResourcePercent, 0, 100, $"{path}.{ruleName}.ResourcePercent", out error) ||
                !TryValidateRange(rule.MinConfidence, rule.MaxConfidence, 0, 1, $"{path}.{ruleName}.Confidence", out error) ||
                !TryValidateRange(rule.MinMetricValue, rule.MaxMetricValue, double.MinValue, double.MaxValue, $"{path}.{ruleName}.MetricValue", out error) ||
                !TryValidateRange(rule.MinWarningCount, rule.MaxWarningCount, 0, int.MaxValue, $"{path}.{ruleName}.WarningCount", out error) ||
                !TryValidateRange(rule.MinUnknownCount, rule.MaxUnknownCount, 0, int.MaxValue, $"{path}.{ruleName}.UnknownCount", out error) ||
                !TryValidateRange(rule.MinAvailableCount, rule.MaxAvailableCount, 0, int.MaxValue, $"{path}.{ruleName}.AvailableCount", out error))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.MetricName) && rule.MinMetricValue is null && rule.MaxMetricValue is null)
            {
                error = $"{path}.{ruleName} defines MetricName but no metric threshold.";
                return false;
            }

            if (requireMatcher && !HasAnyMatcher(rule))
            {
                error = $"{path}.{ruleName} must define at least one matcher or threshold.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryValidateFallbackRule(
        string path,
        FallbackRuleOptions fallback,
        IReadOnlySet<string> allowedDirectiveKinds,
        out string? error)
    {
        if (!fallback.Enabled)
        {
            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(fallback.RuleName))
        {
            error = $"{path} is enabled and must define RuleName.";
            return false;
        }

        var ruleName = fallback.RuleName.Trim();

        if (!TryValidatePolicyRuleSet(path, [fallback], requireMatcher: false, allowedDirectiveKinds, out error))
        {
            return false;
        }

        if (HasAnyMatcher(fallback))
        {
            error = $"{path}.{ruleName} cannot define matchers because fallback rules represent no-match behavior.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fallback.Reason))
        {
            error = $"{path}.{ruleName}.Reason is required for enabled fallback rules.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool HasAnyMatcher(PolicyRuleOptions rule) =>
        rule.MatchLabels.Count > 0 ||
        rule.MatchTypes.Count > 0 ||
        rule.MatchTags.Count > 0 ||
        rule.AllowedThreatSeverities.Count > 0 ||
        rule.MinThreatSeverity is not null ||
        rule.MinRiskSeverity is not null ||
        rule.MatchSuggestedPolicies.Count > 0 ||
        rule.MatchSessionStatuses.Count > 0 ||
        rule.MatchNavigationStatuses.Count > 0 ||
        rule.RequireTransitioning is not null ||
        rule.RequireDestination ||
        rule.RequireIdleNavigation ||
        rule.RequireIdleActivity ||
        rule.RequireNoActiveTarget ||
        rule.RequireActiveTarget ||
        rule.MatchResourceCritical is not null ||
        rule.MatchResourceDegraded is not null ||
        rule.RequireDefensivePosture ||
        rule.MinProgressPercent is not null ||
        rule.MaxProgressPercent is not null ||
        rule.MinResourcePercent is not null ||
        rule.MaxResourcePercent is not null ||
        rule.MinWarningCount is not null ||
        rule.MaxWarningCount is not null ||
        rule.MinUnknownCount is not null ||
        rule.MaxUnknownCount is not null ||
        rule.MinAvailableCount is not null ||
        rule.MaxAvailableCount is not null ||
        rule.MinConfidence is not null ||
        rule.MaxConfidence is not null ||
        !string.IsNullOrWhiteSpace(rule.MetricName);

    private static IReadOnlySet<string> AllowedSiteSelectionDirectiveKinds() =>
        DirectiveKindSet("Observe", "Navigate", "SelectSite", "PauseActivity", "Wait");

    private static IReadOnlySet<string> AllowedThreatResponseDirectiveKinds() =>
        DirectiveKindSet("Observe", "PrioritizeTarget", "AvoidTarget", "PauseActivity", "Withdraw", "Wait");

    private static IReadOnlySet<string> AllowedTargetPrioritizationDirectiveKinds() =>
        DirectiveKindSet("Observe", "SelectTarget", "PrioritizeTarget", "AvoidTarget", "PauseActivity", "Wait");

    private static IReadOnlySet<string> AllowedResourceUsageDirectiveKinds() =>
        DirectiveKindSet("Observe", "UseResource", "ConserveResource", "PauseActivity", "Withdraw", "Wait");

    private static IReadOnlySet<string> AllowedTransitDirectiveKinds() =>
        DirectiveKindSet("Observe", "Navigate", "PauseActivity", "Wait");

    private static IReadOnlySet<string> AllowedAbortDirectiveKinds() =>
        DirectiveKindSet("Observe", "PauseActivity", "Withdraw", "Abort", "Wait");

    private static IReadOnlySet<string> DirectiveKindSet(params string[] directiveKinds) =>
        directiveKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsValidDirectiveKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return new HashSet<string>(
            [
                "None",
                "Observe",
                "Navigate",
                "SelectSite",
                "SelectTarget",
                "PrioritizeTarget",
                "AvoidTarget",
                "UseResource",
                "ConserveResource",
                "PauseActivity",
                "Withdraw",
                "Abort",
                "Wait"
            ],
            StringComparer.OrdinalIgnoreCase).Contains(value.Trim());
    }

    private static bool TryValidateDirectiveKindList(
        IReadOnlyList<string> values,
        bool allowWildcard,
        string propertyName,
        out string? error)
    {
        foreach (var value in values)
        {
            if (allowWildcard && string.Equals(value, "*", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsValidDirectiveKind(value))
            {
                error = $"{propertyName} contains invalid directive kind '{value}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsValidPlanStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return new HashSet<string>(
            [
                "Unknown",
                "Idle",
                "Ready",
                "Blocked",
                "Aborting"
            ],
            StringComparer.OrdinalIgnoreCase).Contains(value.Trim());
    }

    private static bool TryValidateRange(double? min, double? max, double lowerBound, double upperBound, string propertyName, out string? error)
    {
        if (min < lowerBound || min > upperBound || max < lowerBound || max > upperBound)
        {
            error = $"{propertyName} thresholds must be between {lowerBound} and {upperBound}.";
            return false;
        }

        if (min is not null && max is not null && min > max)
        {
            error = $"{propertyName} minimum cannot be greater than maximum.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateRange(int? min, int? max, int lowerBound, int upperBound, string propertyName, out string? error)
    {
        if (min < lowerBound || min > upperBound || max < lowerBound || max > upperBound)
        {
            error = $"{propertyName} thresholds must be between {lowerBound} and {upperBound}.";
            return false;
        }

        if (min is not null && max is not null && min > max)
        {
            error = $"{propertyName} minimum cannot be greater than maximum.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidatePriority(int value, string propertyName, out string? error)
    {
        if (value < 0 || value > 1000)
        {
            error = $"PolicyEngine.{propertyName} must be between 0 and 1000.";
            return false;
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
