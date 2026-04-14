using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Drivers;

public sealed class MockDesktopSessionAdapter : ISessionDriver
{
    private readonly ILogger<MockDesktopSessionAdapter> _logger;

    public MockDesktopSessionAdapter(ILogger<MockDesktopSessionAdapter> logger)
    {
        _logger = logger;
    }

    public async Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogInformation("Simulating desktop attach for session '{DisplayName}'.", snapshot.Definition.DisplayName);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
    }

    public async Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogInformation("Simulating desktop detach for session '{DisplayName}'.", snapshot.Definition.DisplayName);
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogDebug("Simulating work item '{Kind}' for session '{DisplayName}'.", workItem.Kind, snapshot.Definition.DisplayName);

        var simulatedDelay = workItem.Kind switch
        {
            SessionWorkItemKind.Heartbeat => TimeSpan.FromMilliseconds(5),
            _ => TimeSpan.FromMilliseconds(15)
        };

        await Task.Delay(simulatedDelay, cancellationToken).ConfigureAwait(false);
    }
}
