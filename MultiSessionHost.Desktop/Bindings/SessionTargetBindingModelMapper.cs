using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

internal static class SessionTargetBindingModelMapper
{
    public static DesktopTargetProfile MapProfile(DesktopTargetProfileOptions options) =>
        new(
            options.ProfileName.Trim(),
            options.Kind,
            options.ProcessName.Trim(),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            NormalizeMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    public static SessionTargetBinding MapBinding(SessionTargetBindingOptions options) =>
        NormalizeBinding(
            new SessionTargetBinding(
                new SessionId(options.SessionId),
                options.TargetProfileName.Trim(),
                options.Variables.ToDictionary(
                    static pair => pair.Key.Trim(),
                    static pair => pair.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
                options.Overrides is null ? null : MapOverrides(options.Overrides)));

    public static SessionTargetBinding NormalizeBinding(SessionTargetBinding binding) =>
        new(
            binding.SessionId,
            binding.TargetProfileName.Trim(),
            binding.Variables.ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            binding.Overrides is null ? null : NormalizeOverride(binding.Overrides));

    public static DesktopTargetProfileOverride MapOverrides(DesktopTargetProfileOverrideOptions options) =>
        new(
            TrimToNull(options.ProcessName),
            TrimToNull(options.WindowTitleFragment),
            TrimToNull(options.CommandLineFragmentTemplate),
            TrimToNull(options.BaseAddressTemplate),
            options.MatchingMode,
            NormalizeMetadata(options.Metadata),
            options.SupportsUiSnapshots,
            options.SupportsStateEndpoint);

    public static DesktopTargetProfileOverride NormalizeOverride(DesktopTargetProfileOverride profileOverride) =>
        new(
            TrimToNull(profileOverride.ProcessName),
            TrimToNull(profileOverride.WindowTitleFragment),
            TrimToNull(profileOverride.CommandLineFragmentTemplate),
            TrimToNull(profileOverride.BaseAddressTemplate),
            profileOverride.MatchingMode,
            NormalizeMetadata(profileOverride.Metadata),
            profileOverride.SupportsUiSnapshots,
            profileOverride.SupportsStateEndpoint);

    public static IReadOnlyDictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?> metadata) =>
        metadata.ToDictionary(
            static pair => pair.Key.Trim(),
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
