using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class ConfiguredDesktopTargetProfileResolver : IDesktopTargetProfileResolver
{
    private readonly IReadOnlyDictionary<string, DesktopTargetProfile> _profilesByName;
    private readonly IReadOnlyDictionary<SessionId, SessionTargetBinding> _bindingsBySessionId;

    public ConfiguredDesktopTargetProfileResolver(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _profilesByName = options.DesktopTargets
            .Select(MapProfile)
            .ToDictionary(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase);
        _bindingsBySessionId = options.SessionTargetBindings
            .Select(MapBinding)
            .ToDictionary(static binding => binding.SessionId);
    }

    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() =>
        _profilesByName.Values
            .OrderBy(static profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public DesktopTargetProfile? TryGetProfile(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return _profilesByName.TryGetValue(profileName.Trim(), out var profile) ? profile : null;
    }

    public SessionTargetBinding? TryGetBinding(SessionId sessionId) =>
        _bindingsBySessionId.TryGetValue(sessionId, out var binding) ? binding : null;

    public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var binding = TryGetBinding(snapshot.SessionId)
            ?? throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a SessionTargetBinding.");
        var profile = TryGetProfile(binding.TargetProfileName)
            ?? throw new InvalidOperationException($"Target profile '{binding.TargetProfileName}' could not be found for session '{snapshot.SessionId}'.");
        var effectiveProfile = ApplyOverrides(profile, binding.Overrides);
        var variables = BuildVariables(snapshot.SessionId, binding.Variables);
        var target = new DesktopSessionTarget(
            snapshot.SessionId,
            effectiveProfile.ProfileName,
            effectiveProfile.Kind,
            effectiveProfile.MatchingMode,
            RenderRequired(effectiveProfile.ProcessName, variables, "ProcessName"),
            RenderOptional(effectiveProfile.WindowTitleFragment, variables),
            RenderOptional(effectiveProfile.CommandLineFragmentTemplate, variables),
            RenderUri(effectiveProfile.BaseAddressTemplate, variables),
            RenderMetadata(effectiveProfile.Metadata, variables));

        return new ResolvedDesktopTargetContext(snapshot.SessionId, effectiveProfile, binding, target, variables);
    }

    private static DesktopTargetProfile MapProfile(DesktopTargetProfileOptions options) =>
        new(
            options.ProfileName.Trim(),
            options.Kind,
            options.ProcessName.Trim(),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            CreateMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    private static SessionTargetBinding MapBinding(SessionTargetBindingOptions options) =>
        new(
            new SessionId(options.SessionId),
            options.TargetProfileName.Trim(),
            options.Variables.ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase),
            options.Overrides is null ? null : MapOverrides(options.Overrides));

    private static DesktopTargetProfileOverride MapOverrides(DesktopTargetProfileOverrideOptions options) =>
        new(
            TrimToNull(options.ProcessName),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            CreateMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    private static DesktopTargetProfile ApplyOverrides(DesktopTargetProfile profile, DesktopTargetProfileOverride? overrides)
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

    private static IReadOnlyDictionary<string, string> BuildVariables(SessionId sessionId, IReadOnlyDictionary<string, string> bindingVariables)
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

    private static string RenderRequired(string template, IReadOnlyDictionary<string, string> variables, string fieldName)
    {
        var rendered = SessionHostTemplateRenderer.Render(template, variables).Trim();
        return rendered.Length == 0
            ? throw new InvalidOperationException($"The rendered {fieldName} is empty.")
            : rendered;
    }

    private static string? RenderOptional(string? template, IReadOnlyDictionary<string, string> variables) =>
        string.IsNullOrWhiteSpace(template)
            ? null
            : SessionHostTemplateRenderer.Render(template, variables).Trim();

    private static Uri? RenderUri(string? template, IReadOnlyDictionary<string, string> variables)
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

    private static IReadOnlyDictionary<string, string?> RenderMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        IReadOnlyDictionary<string, string> variables) =>
        metadata.ToDictionary(
            static pair => pair.Key,
            pair => string.IsNullOrWhiteSpace(pair.Value) ? pair.Value : SessionHostTemplateRenderer.Render(pair.Value, variables),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string?> CreateMetadata(IReadOnlyDictionary<string, string?> metadata) =>
        metadata.ToDictionary(
            static pair => pair.Key.Trim(),
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
