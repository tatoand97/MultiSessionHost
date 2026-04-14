using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Core.State;

namespace MultiSessionHost.Infrastructure.Lifecycle;

public sealed class DefaultSessionLifecycleManager : ISessionLifecycleManager
{
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IWorkQueue _workQueue;
    private readonly ISessionDriver _sessionDriver;
    private readonly IClock _clock;
    private readonly IHealthReporter _healthReporter;
    private readonly ILogger<DefaultSessionLifecycleManager> _logger;
    private readonly ConcurrentDictionary<SessionId, SessionExecutionContext> _executionContexts = new();
    private readonly ConcurrentDictionary<SessionId, SemaphoreSlim> _sessionGates = new();

    public DefaultSessionLifecycleManager(
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        IWorkQueue workQueue,
        ISessionDriver sessionDriver,
        IClock clock,
        IHealthReporter healthReporter,
        ILogger<DefaultSessionLifecycleManager> logger)
    {
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _workQueue = workQueue;
        _sessionDriver = sessionDriver;
        _clock = clock;
        _healthReporter = healthReporter;
        _logger = logger;
    }

    public async Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StartSessionCoreAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var definition = GetRequiredDefinition(sessionId);
            var state = await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var previousStatus = state.CurrentStatus;

            state = await _sessionStateStore.UpdateAsync(
                sessionId,
                current => current with { DesiredStatus = SessionStatus.Stopped },
                cancellationToken).ConfigureAwait(false);

            if (state.CurrentStatus == SessionStatus.Stopped)
            {
                return;
            }

            if (state.CurrentStatus == SessionStatus.Created)
            {
                var stoppedFromCreated = await _sessionStateStore.UpdateAsync(
                    sessionId,
                    current => SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Stopped }, SessionStatus.Stopped, _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                _healthReporter.RecordTransition(sessionId, previousStatus, stoppedFromCreated.CurrentStatus);
                return;
            }

            if (state.CurrentStatus != SessionStatus.Stopping && state.CurrentStatus != SessionStatus.Faulted)
            {
                state = await _sessionStateStore.UpdateAsync(
                    sessionId,
                    current => SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Stopped }, SessionStatus.Stopping, _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                _healthReporter.RecordTransition(sessionId, previousStatus, state.CurrentStatus);
            }

            await _workQueue.CompleteAsync(sessionId).ConfigureAwait(false);
            await _workQueue.WaitUntilEmptyAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await WaitForInFlightZeroAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (_executionContexts.TryRemove(sessionId, out var executionContext))
            {
                await executionContext.WhenComplete.ConfigureAwait(false);
            }

            await _sessionDriver.DetachAsync(
                BuildSnapshot(definition, await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);

            var stopped = await _sessionStateStore.UpdateAsync(
                sessionId,
                current =>
                {
                    var nextStatus = current.CurrentStatus == SessionStatus.Faulted
                        ? SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Stopped }, SessionStatus.Stopped, _clock.UtcNow)
                        : SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Stopped }, SessionStatus.Stopped, _clock.UtcNow);

                    return nextStatus with
                    {
                        DesiredStatus = SessionStatus.Stopped,
                        ObservedStatus = SessionStatus.Stopped
                    };
                },
                cancellationToken).ConfigureAwait(false);

            _healthReporter.RecordTransition(sessionId, state.CurrentStatus, stopped.CurrentStatus);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var state = await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (state.CurrentStatus is not (SessionStatus.Starting or SessionStatus.Running))
            {
                return;
            }

            var previousStatus = state.CurrentStatus;

            await _sessionStateStore.UpdateAsync(
                sessionId,
                current => current with { DesiredStatus = SessionStatus.Paused },
                cancellationToken).ConfigureAwait(false);

            await _workQueue.WaitUntilEmptyAsync(sessionId, cancellationToken).ConfigureAwait(false);
            await WaitForInFlightZeroAsync(sessionId, cancellationToken).ConfigureAwait(false);

            var paused = await _sessionStateStore.UpdateAsync(
                sessionId,
                current => SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Paused }, SessionStatus.Paused, _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            _healthReporter.RecordTransition(sessionId, previousStatus, paused.CurrentStatus);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var gate = _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var state = await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (state.CurrentStatus == SessionStatus.Paused)
            {
                var resumed = await _sessionStateStore.UpdateAsync(
                    sessionId,
                    current => SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Running }, SessionStatus.Running, _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                _healthReporter.RecordTransition(sessionId, SessionStatus.Paused, resumed.CurrentStatus);
                return;
            }

            if (state.CurrentStatus is SessionStatus.Created or SessionStatus.Stopped or SessionStatus.Faulted)
            {
                await StartSessionCoreAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task EnqueueAsync(SessionId sessionId, SessionWorkItem workItem, CancellationToken cancellationToken) =>
        _workQueue.EnqueueAsync(sessionId, workItem, cancellationToken).AsTask();

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in _sessionRegistry.GetAll())
        {
            await StopSessionAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StartSessionCoreAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var definition = GetRequiredDefinition(sessionId);
        var state = await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (state.CurrentStatus is SessionStatus.Starting or SessionStatus.Running)
        {
            await _sessionStateStore.UpdateAsync(
                sessionId,
                current => current with { DesiredStatus = SessionStatus.Running },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state.CurrentStatus == SessionStatus.Paused)
        {
            var resumed = await _sessionStateStore.UpdateAsync(
                sessionId,
                current => SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Running }, SessionStatus.Running, _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);

            _healthReporter.RecordTransition(sessionId, SessionStatus.Paused, resumed.CurrentStatus);
            return;
        }

        if (_executionContexts.TryRemove(sessionId, out var existingContext))
        {
            await existingContext.WhenComplete.ConfigureAwait(false);
        }

        var previousStatus = state.CurrentStatus;
        var starting = await _sessionStateStore.UpdateAsync(
            sessionId,
            current =>
            {
                var transitioned = SessionStateMachine.Transition(current with { DesiredStatus = SessionStatus.Running }, SessionStatus.Starting, _clock.UtcNow);
                return transitioned with
                {
                    DesiredStatus = SessionStatus.Running,
                    RetryPolicy = current.RetryPolicy.Reset(),
                    ObservedStatus = SessionStatus.Starting,
                    LastError = null
                };
            },
            cancellationToken).ConfigureAwait(false);

        _healthReporter.RecordTransition(sessionId, previousStatus, starting.CurrentStatus);

        await _workQueue.ResetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (definition.StartupDelay > TimeSpan.Zero)
        {
            await Task.Delay(definition.StartupDelay, cancellationToken).ConfigureAwait(false);
        }

        await _sessionDriver.AttachAsync(BuildSnapshot(definition, starting), cancellationToken).ConfigureAwait(false);

        var processingTasks = Enumerable
            .Range(0, definition.MaxParallelWorkItems)
            .Select(_ => Task.Run(() => ProcessSessionQueueAsync(definition), CancellationToken.None))
            .ToArray();

        _executionContexts[sessionId] = new SessionExecutionContext(processingTasks);

        var running = await _sessionStateStore.UpdateAsync(
            sessionId,
            current => SessionStateMachine.Transition(
                current with
                {
                    DesiredStatus = SessionStatus.Running,
                    RetryPolicy = current.RetryPolicy.Reset(),
                    ObservedStatus = SessionStatus.Running
                },
                SessionStatus.Running,
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);

        _healthReporter.RecordTransition(sessionId, SessionStatus.Starting, running.CurrentStatus);
    }

    private async Task ProcessSessionQueueAsync(SessionDefinition definition)
    {
        await foreach (var workItem in _workQueue.ReadAllAsync(definition.Id, CancellationToken.None))
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = definition.Id.Value });

            await _sessionStateStore.UpdateAsync(
                definition.Id,
                current => current with { InFlightWorkItems = current.InFlightWorkItems + 1 },
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                var snapshot = BuildSnapshot(definition, await GetRequiredStateAsync(definition.Id, CancellationToken.None).ConfigureAwait(false));
                await _sessionDriver.ExecuteWorkItemAsync(snapshot, workItem, CancellationToken.None).ConfigureAwait(false);

                var completedAt = _clock.UtcNow;
                var successState = await _sessionStateStore.UpdateAsync(
                    definition.Id,
                    current => current with
                    {
                        RetryPolicy = current.RetryPolicy.Reset(),
                        ObservedStatus = current.CurrentStatus,
                        LastError = null,
                        LastWorkItemCompletedAtUtc = completedAt,
                        LastHeartbeatUtc = workItem.Kind == SessionWorkItemKind.Heartbeat ? completedAt : current.LastHeartbeatUtc
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (workItem.Kind == SessionWorkItemKind.Tick)
                {
                    _healthReporter.RecordTick(definition.Id);
                }

                if (workItem.Kind == SessionWorkItemKind.Heartbeat)
                {
                    _healthReporter.RecordHeartbeat(
                        new SessionHeartbeat(
                            definition.Id,
                            completedAt,
                            successState.ObservedStatus));
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Session '{SessionId}' failed while processing work item '{WorkItemId}'.", definition.Id, workItem.WorkItemId);
                _healthReporter.RecordError(definition.Id, exception);

                var handledFailure = await _sessionStateStore.UpdateAsync(
                    definition.Id,
                    current =>
                    {
                        var failureAt = _clock.UtcNow;
                        var retryState = current.RetryPolicy.RegisterFailure(definition, failureAt);

                        if (retryState.HasExceeded(definition))
                        {
                            return SessionStateMachine.Transition(
                                current with
                                {
                                    DesiredStatus = SessionStatus.Faulted,
                                    RetryPolicy = retryState,
                                    ObservedStatus = SessionStatus.Faulted,
                                    LastError = exception.Message,
                                    LastErrorAtUtc = failureAt
                                },
                                SessionStatus.Faulted,
                                failureAt,
                                exception.Message);
                        }

                        return current with
                        {
                            RetryPolicy = retryState,
                            ObservedStatus = SessionStatus.Faulted,
                            LastError = exception.Message,
                            LastErrorAtUtc = failureAt
                        };
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (handledFailure.CurrentStatus == SessionStatus.Faulted)
                {
                    await _workQueue.CompleteAsync(definition.Id).ConfigureAwait(false);
                }
                else
                {
                    _healthReporter.RecordRetry(definition.Id);
                }
            }
            finally
            {
                await _sessionStateStore.UpdateAsync(
                    definition.Id,
                    current => current with { InFlightWorkItems = Math.Max(0, current.InFlightWorkItems - 1) },
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task WaitForInFlightZeroAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var state = await GetRequiredStateAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (state.InFlightWorkItems == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }
    }

    private SessionDefinition GetRequiredDefinition(SessionId sessionId) =>
        _sessionRegistry.GetById(sessionId) ?? throw new InvalidOperationException($"Session '{sessionId}' is not registered.");

    private async ValueTask<SessionRuntimeState> GetRequiredStateAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        await _sessionStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Runtime state for session '{sessionId}' was not found.");

    private SessionSnapshot BuildSnapshot(SessionDefinition definition, SessionRuntimeState state) =>
        new(definition, state, _workQueue.GetPendingCount(definition.Id));

    private sealed class SessionExecutionContext
    {
        public SessionExecutionContext(Task[] workers)
        {
            Workers = workers;
        }

        public Task[] Workers { get; }

        public Task WhenComplete => Task.WhenAll(Workers);
    }
}
