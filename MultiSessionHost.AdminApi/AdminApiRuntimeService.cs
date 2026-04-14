using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;

namespace MultiSessionHost.AdminApi;

public sealed class AdminApiRuntimeService : BackgroundService
{
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly SessionHostOptions _options;
    private readonly ILogger<AdminApiRuntimeService> _logger;

    public AdminApiRuntimeService(
        ISessionCoordinator sessionCoordinator,
        SessionHostOptions options,
        ILogger<AdminApiRuntimeService> logger)
    {
        _sessionCoordinator = sessionCoordinator;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _sessionCoordinator.InitializeAsync(stoppingToken).ConfigureAwait(false);
        using var schedulerTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.SchedulerIntervalMs));

        try
        {
            while (await schedulerTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _sessionCoordinator.RunSchedulerCycleAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Admin API runtime service is stopping.");
        }
        finally
        {
            await _sessionCoordinator.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
