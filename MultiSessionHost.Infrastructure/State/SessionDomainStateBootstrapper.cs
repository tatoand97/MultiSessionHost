using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.State;

public sealed class SessionDomainStateBootstrapper : ISessionDomainStateBootstrapper
{
    private readonly SessionHostOptions _options;
    private readonly ISessionDomainStateStore _domainStateStore;
    private readonly IClock _clock;

    public SessionDomainStateBootstrapper(
        SessionHostOptions options,
        ISessionDomainStateStore domainStateStore,
        IClock clock)
    {
        _options = options;
        _domainStateStore = domainStateStore;
        _clock = clock;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        foreach (var definition in _options.ToSessionDefinitions())
        {
            await _domainStateStore.InitializeAsync(
                SessionDomainState.CreateBootstrap(definition.Id, now),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
