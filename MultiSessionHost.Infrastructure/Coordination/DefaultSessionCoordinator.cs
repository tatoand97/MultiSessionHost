using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Coordination;

public sealed class DefaultSessionCoordinator : ISessionCoordinator
{
    private readonly SessionHostOptions _options;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly ISessionScheduler _sessionScheduler;
    private readonly ISessionLifecycleManager _sessionLifecycleManager;
    private readonly IWorkQueue _workQueue;
    private readonly IClock _clock;
    private readonly IHealthReporter _healthReporter;
    private readonly ILogger<DefaultSessionCoordinator> _logger;
    private int _initialized;
    private int _shutdownRequested;

    public DefaultSessionCoordinator(
        SessionHostOptions options,
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        ISessionScheduler sessionScheduler,
        ISessionLifecycleManager sessionLifecycleManager,
        IWorkQueue workQueue,
        IClock clock,
        IHealthReporter healthReporter,
        ILogger<DefaultSessionCoordinator> logger)
    {
        _options = options;
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _sessionScheduler = sessionScheduler;
        _sessionLifecycleManager = sessionLifecycleManager;
        _workQueue = workQueue;
        _clock = clock;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var now = _clock.UtcNow;

        foreach (var definition in _options.ToSessionDefinitions())
        {
            await _sessionRegistry.RegisterAsync(definition, cancellationToken).ConfigureAwait(false);
            await _sessionStateStore.InitializeAsync(SessionRuntimeState.Create(definition, now), cancellationToken).ConfigureAwait(false);
            _healthReporter.RecordRegistration(definition);
        }

        _logger.LogInformation("Registered {SessionCount} session definitions.", _sessionRegistry.GetAll().Count);
    }

    public async Task RunSchedulerCycleAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _shutdownRequested) == 1)
        {
            return;
        }

        var now = _clock.UtcNow;
        var decisions = _sessionScheduler.GetDecisions(GetSessions(), _options.MaxGlobalParallelSessions, now);

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (decision.DecisionType)
            {
                case SchedulerDecisionType.Start:
                    await _sessionLifecycleManager.StartSessionAsync(decision.SessionId, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulerDecisionType.EnqueueWork when decision.WorkItem is not null:
                    await _sessionStateStore.UpdateAsync(
                        decision.SessionId,
                        state => state with { LastWorkItemScheduledAtUtc = now },
                        cancellationToken).ConfigureAwait(false);

                    await _sessionLifecycleManager.EnqueueAsync(decision.SessionId, decision.WorkItem, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    public Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _sessionLifecycleManager.StartSessionAsync(sessionId, cancellationToken);

    public Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _sessionLifecycleManager.StopSessionAsync(sessionId, cancellationToken);

    public Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _sessionLifecycleManager.PauseSessionAsync(sessionId, cancellationToken);

    public Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _sessionLifecycleManager.ResumeSessionAsync(sessionId, cancellationToken);

    public IReadOnlyCollection<SessionSnapshot> GetSessions()
    {
        var definitions = _sessionRegistry.GetAll();
        var states = _sessionStateStore.GetAll().ToDictionary(static state => state.SessionId);

        return definitions
            .Select(
                definition =>
                {
                    var state = states[definition.Id];
                    return new SessionSnapshot(definition, state, _workQueue.GetPendingCount(definition.Id));
                })
            .ToArray();
    }

    public SessionSnapshot? GetSession(SessionId sessionId) =>
        GetSessions().FirstOrDefault(snapshot => snapshot.SessionId == sessionId);

    public ProcessHealthSnapshot GetProcessHealth() =>
        _healthReporter.CreateSnapshot(GetSessions(), _clock.UtcNow);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
        {
            return;
        }

        _logger.LogInformation("Coordinator shutdown requested. Draining sessions.");
        await _sessionLifecycleManager.StopAllAsync(cancellationToken).ConfigureAwait(false);
    }
}
