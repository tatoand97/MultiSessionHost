namespace MultiSessionHost.Core.Interfaces;

public interface ISessionDomainStateBootstrapper
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
