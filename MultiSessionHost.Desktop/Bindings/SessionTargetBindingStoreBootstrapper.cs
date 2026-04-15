using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Desktop.Interfaces;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class SessionTargetBindingStoreBootstrapper : ISessionTargetBindingBootstrapper
{
    private readonly ISessionTargetBindingStore _bindingStore;
    private readonly ISessionTargetBindingPersistence _persistence;
    private readonly IDesktopTargetProfileCatalog _profileCatalog;
    private readonly IReadOnlySet<string> _configuredSessionIds;
    private int _initialized;

    public SessionTargetBindingStoreBootstrapper(
        SessionHostOptions options,
        ISessionTargetBindingStore bindingStore,
        ISessionTargetBindingPersistence persistence,
        IDesktopTargetProfileCatalog profileCatalog)
    {
        ArgumentNullException.ThrowIfNull(options);

        _bindingStore = bindingStore;
        _persistence = persistence;
        _profileCatalog = profileCatalog;
        _configuredSessionIds = options.Sessions
            .Select(static session => session.SessionId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var persistedBindings = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        var seenSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in persistedBindings)
        {
            if (!seenSessions.Add(binding.SessionId.Value))
            {
                throw new InvalidOperationException($"The persisted binding store contains duplicate session '{binding.SessionId}'.");
            }

            if (!SessionTargetBindingValidation.TryValidate(binding, _configuredSessionIds, _profileCatalog, out var error))
            {
                throw new InvalidOperationException($"The persisted binding for session '{binding.SessionId}' is invalid. {error}");
            }

            await _bindingStore.UpsertAsync(binding, cancellationToken).ConfigureAwait(false);
        }
    }
}
