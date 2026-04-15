namespace MultiSessionHost.Desktop.Bindings;

public interface ISessionTargetBindingBootstrapper
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
