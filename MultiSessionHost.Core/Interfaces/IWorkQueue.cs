using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface IWorkQueue
{
    ValueTask ResetSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask EnqueueAsync(SessionId sessionId, SessionWorkItem workItem, CancellationToken cancellationToken);

    IAsyncEnumerable<SessionWorkItem> ReadAllAsync(SessionId sessionId, CancellationToken cancellationToken);

    ValueTask CompleteAsync(SessionId sessionId);

    int GetPendingCount(SessionId sessionId);

    Task WaitUntilEmptyAsync(SessionId sessionId, CancellationToken cancellationToken);
}
