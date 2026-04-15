using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class ConfiguredDesktopTargetProfileResolver : IDesktopTargetProfileResolver
{
    private readonly IDesktopTargetProfileCatalog _profileCatalog;
    private readonly ISessionTargetBindingStore _bindingStore;

    public ConfiguredDesktopTargetProfileResolver(
        IDesktopTargetProfileCatalog profileCatalog,
        ISessionTargetBindingStore bindingStore)
    {
        _profileCatalog = profileCatalog;
        _bindingStore = bindingStore;
    }

    public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => _profileCatalog.GetProfiles();

    public DesktopTargetProfile? TryGetProfile(string profileName) => _profileCatalog.TryGetProfile(profileName);

    public SessionTargetBinding? TryGetBinding(SessionId sessionId) =>
        _bindingStore.GetAsync(sessionId, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

    public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var binding = TryGetBinding(snapshot.SessionId)
            ?? throw new InvalidOperationException($"Session '{snapshot.SessionId}' does not have a SessionTargetBinding.");
        var profile = TryGetProfile(binding.TargetProfileName)
            ?? throw new InvalidOperationException($"Target profile '{binding.TargetProfileName}' could not be found for session '{snapshot.SessionId}'.");
        var effectiveProfile = DesktopTargetProfileResolution.ApplyOverrides(profile, binding.Overrides);
        var variables = DesktopTargetProfileResolution.BuildVariables(snapshot.SessionId, binding.Variables);
        var target = new DesktopSessionTarget(
            snapshot.SessionId,
            effectiveProfile.ProfileName,
            effectiveProfile.Kind,
            effectiveProfile.MatchingMode,
            DesktopTargetProfileResolution.RenderRequired(effectiveProfile.ProcessName, variables, "ProcessName"),
            DesktopTargetProfileResolution.RenderOptional(effectiveProfile.WindowTitleFragment, variables),
            DesktopTargetProfileResolution.RenderOptional(effectiveProfile.CommandLineFragmentTemplate, variables),
            DesktopTargetProfileResolution.RenderUri(effectiveProfile.BaseAddressTemplate, variables),
            DesktopTargetProfileResolution.RenderMetadata(effectiveProfile.Metadata, variables));

        return new ResolvedDesktopTargetContext(snapshot.SessionId, effectiveProfile, binding, target, variables);
    }
}
