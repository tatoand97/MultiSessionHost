using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Queues;

public sealed class ChannelBasedWorkQueue : IWorkQueue
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionQueue> _sessionQueues = [];

    public ValueTask ResetSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sessionQueues[sessionId] = new SessionQueue();
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask EnqueueAsync(SessionId sessionId, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        SessionQueue sessionQueue;

        lock (_gate)
        {
            sessionQueue = GetRequiredQueue(sessionId);

            if (sessionQueue.PendingCount == 0)
            {
                sessionQueue.EmptySignal = CreatePendingSignal();
            }

            sessionQueue.PendingCount++;
        }

        await sessionQueue.Channel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<SessionWorkItem> ReadAllAsync(
        SessionId sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SessionQueue sessionQueue;

        lock (_gate)
        {
            sessionQueue = GetRequiredQueue(sessionId);
        }

        await foreach (var workItem in sessionQueue.Channel.Reader.ReadAllAsync(cancellationToken))
        {
            MarkDequeued(sessionQueue);
            yield return workItem;
        }
    }

    public ValueTask CompleteAsync(SessionId sessionId)
    {
        lock (_gate)
        {
            if (_sessionQueues.TryGetValue(sessionId, out var sessionQueue))
            {
                sessionQueue.Channel.Writer.TryComplete();
            }
        }

        return ValueTask.CompletedTask;
    }

    public int GetPendingCount(SessionId sessionId)
    {
        lock (_gate)
        {
            return _sessionQueues.TryGetValue(sessionId, out var sessionQueue)
                ? sessionQueue.PendingCount
                : 0;
        }
    }

    public async Task WaitUntilEmptyAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        Task waitTask;

        lock (_gate)
        {
            waitTask = _sessionQueues.TryGetValue(sessionId, out var sessionQueue)
                ? sessionQueue.EmptySignal.Task
                : Task.CompletedTask;
        }

        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void MarkDequeued(SessionQueue sessionQueue)
    {
        lock (_gate)
        {
            sessionQueue.PendingCount = Math.Max(0, sessionQueue.PendingCount - 1);

            if (sessionQueue.PendingCount == 0)
            {
                sessionQueue.EmptySignal.TrySetResult(true);
            }
        }
    }

    private SessionQueue GetRequiredQueue(SessionId sessionId) =>
        _sessionQueues.TryGetValue(sessionId, out var sessionQueue)
            ? sessionQueue
            : throw new InvalidOperationException($"Queue for session '{sessionId}' has not been created.");

    private static TaskCompletionSource<bool> CreatePendingSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CreateCompletedSignal()
    {
        var taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        taskCompletionSource.TrySetResult(true);
        return taskCompletionSource;
    }

    private sealed class SessionQueue
    {
        public SessionQueue()
        {
            Channel = System.Threading.Channels.Channel.CreateUnbounded<SessionWorkItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            EmptySignal = CreateCompletedSignal();
        }

        public Channel<SessionWorkItem> Channel { get; }

        public int PendingCount { get; set; }

        public TaskCompletionSource<bool> EmptySignal { get; set; }
    }
}
