using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Drivers;

public sealed class NoOpSessionDriver : ISessionDriver
{
    private readonly ILogger<NoOpSessionDriver> _logger;

    public NoOpSessionDriver(ILogger<NoOpSessionDriver> logger)
    {
        _logger = logger;
    }

    public Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogInformation("Attaching no-op driver to session '{DisplayName}'.", snapshot.Definition.DisplayName);
        return Task.CompletedTask;
    }

    public Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogInformation("Detaching no-op driver from session '{DisplayName}'.", snapshot.Definition.DisplayName);
        return Task.CompletedTask;
    }

    public Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = snapshot.SessionId.Value });
        _logger.LogDebug("No-op driver handled '{Kind}' for session '{DisplayName}'.", workItem.Kind, snapshot.Definition.DisplayName);
        return Task.CompletedTask;
    }
}
