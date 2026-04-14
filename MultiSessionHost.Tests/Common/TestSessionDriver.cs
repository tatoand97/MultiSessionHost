using System.Collections.Concurrent;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Tests.Common;

public sealed class TestSessionDriver : ISessionDriver
{
    private readonly Func<SessionSnapshot, SessionWorkItem, bool>? _shouldFail;
    private readonly Func<SessionSnapshot, SessionWorkItem, Task>? _beforeExecute;

    public TestSessionDriver(
        TimeSpan? workDelay = null,
        Func<SessionSnapshot, SessionWorkItem, bool>? shouldFail = null,
        Func<SessionSnapshot, SessionWorkItem, Task>? beforeExecute = null)
    {
        WorkDelay = workDelay ?? TimeSpan.Zero;
        _shouldFail = shouldFail;
        _beforeExecute = beforeExecute;
    }

    public TimeSpan WorkDelay { get; }

    public ConcurrentDictionary<SessionId, int> Attachments { get; } = new();

    public ConcurrentDictionary<SessionId, int> Detachments { get; } = new();

    public ConcurrentDictionary<SessionId, int> Executions { get; } = new();

    public async Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        Attachments.AddOrUpdate(snapshot.SessionId, 1, static (_, current) => current + 1);
        await Task.CompletedTask;
    }

    public async Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        Detachments.AddOrUpdate(snapshot.SessionId, 1, static (_, current) => current + 1);
        await Task.CompletedTask;
    }

    public async Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        Executions.AddOrUpdate(snapshot.SessionId, 1, static (_, current) => current + 1);

        if (_beforeExecute is not null)
        {
            await _beforeExecute(snapshot, workItem);
        }

        if (WorkDelay > TimeSpan.Zero)
        {
            await Task.Delay(WorkDelay, cancellationToken);
        }

        if (_shouldFail?.Invoke(snapshot, workItem) == true)
        {
            throw new InvalidOperationException($"Injected failure for session '{snapshot.SessionId}'.");
        }
    }
}
