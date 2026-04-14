using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.Queues;

namespace MultiSessionHost.Tests.Queues;

public sealed class ChannelBasedWorkQueueTests
{
    [Fact]
    public async Task WaitUntilEmptyAsync_CompletesAfterReaderConsumesBacklog()
    {
        var queue = new ChannelBasedWorkQueue();
        var sessionId = new SessionId("alpha");
        var consumed = new List<SessionWorkItem>();

        await queue.ResetSessionAsync(sessionId, CancellationToken.None);
        await queue.EnqueueAsync(sessionId, SessionWorkItem.Create(sessionId, SessionWorkItemKind.Tick, DateTimeOffset.UtcNow, "tick-1"), CancellationToken.None);
        await queue.EnqueueAsync(sessionId, SessionWorkItem.Create(sessionId, SessionWorkItemKind.Heartbeat, DateTimeOffset.UtcNow, "heartbeat-1"), CancellationToken.None);

        var consumer = Task.Run(
            async () =>
            {
                await foreach (var item in queue.ReadAllAsync(sessionId, CancellationToken.None))
                {
                    consumed.Add(item);

                    if (consumed.Count == 2)
                    {
                        break;
                    }
                }
            });

        await queue.WaitUntilEmptyAsync(sessionId, CancellationToken.None);
        await consumer;

        Assert.Equal(2, consumed.Count);
        Assert.Equal(0, queue.GetPendingCount(sessionId));
    }
}
