namespace MultiSessionHost.AdminDesktop.Services;

public interface IRefreshCoordinator
{
    Task RunOnceAsync(Func<CancellationToken, Task> refresh, CancellationToken cancellationToken);

    Task RunPeriodicAsync(TimeSpan interval, Func<CancellationToken, Task> refresh, CancellationToken cancellationToken);
}

public sealed class RefreshCoordinator : IRefreshCoordinator
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public async Task RunOnceAsync(Func<CancellationToken, Task> refresh, CancellationToken cancellationToken)
    {
        if (!await refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await refresh(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public async Task RunPeriodicAsync(TimeSpan interval, Func<CancellationToken, Task> refresh, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RunOnceAsync(refresh, cancellationToken).ConfigureAwait(false);
        }
    }
}
