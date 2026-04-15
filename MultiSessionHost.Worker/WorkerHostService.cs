using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Persistence;

namespace MultiSessionHost.Worker;

public sealed class WorkerHostService : BackgroundService
{
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly ISessionTargetBindingBootstrapper _bindingBootstrapper;
    private readonly IRuntimePersistenceCoordinator _runtimePersistenceCoordinator;
    private readonly SessionHostOptions _options;
    private readonly ILogger<WorkerHostService> _logger;

    public WorkerHostService(
        ISessionCoordinator sessionCoordinator,
        ISessionTargetBindingBootstrapper bindingBootstrapper,
        IRuntimePersistenceCoordinator runtimePersistenceCoordinator,
        SessionHostOptions options,
        ILogger<WorkerHostService> logger)
    {
        _sessionCoordinator = sessionCoordinator;
        _bindingBootstrapper = bindingBootstrapper;
        _runtimePersistenceCoordinator = runtimePersistenceCoordinator;
        _options = options;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MultiSessionHost worker.");

        if (_options.EnableAdminApi)
        {
            _logger.LogInformation(
                "Admin API is enabled and running in-process at {AdminApiUrl}.",
                _options.AdminApiUrl);
        }

        await _bindingBootstrapper.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _sessionCoordinator.InitializeAsync(stoppingToken).ConfigureAwait(false);
        await _runtimePersistenceCoordinator.RehydrateAsync(stoppingToken).ConfigureAwait(false);

        using var schedulerTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.SchedulerIntervalMs));
        using var healthTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.HealthLogIntervalMs));

        var schedulerTask = RunSchedulerLoopAsync(schedulerTimer, stoppingToken);
        var healthTask = RunHealthLoopAsync(healthTimer, stoppingToken);

        try
        {
            await Task.WhenAll(schedulerTask, healthTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker execution cancelled.");
        }
        finally
        {
            await _runtimePersistenceCoordinator.FlushAllAsync(CancellationToken.None).ConfigureAwait(false);
            await _sessionCoordinator.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MultiSessionHost worker.");
        await _runtimePersistenceCoordinator.FlushAllAsync(cancellationToken).ConfigureAwait(false);
        await _sessionCoordinator.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunSchedulerLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await _sessionCoordinator.RunSchedulerCycleAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunHealthLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var health = _sessionCoordinator.GetProcessHealth();

            _logger.LogInformation(
                "Health: ActiveSessions={ActiveSessions}, FaultedSessions={FaultedSessions}, Ticks={Ticks}, Errors={Errors}, Retries={Retries}, Heartbeats={Heartbeats}",
                health.ActiveSessions,
                health.FaultedSessions,
                health.TotalTicksExecuted,
                health.TotalErrors,
                health.TotalRetries,
                health.TotalHeartbeatsEmitted);
        }
    }
}
