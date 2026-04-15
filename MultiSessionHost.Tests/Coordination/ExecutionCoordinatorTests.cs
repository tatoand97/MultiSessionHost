using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Infrastructure.Time;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Coordination;

public sealed class ExecutionCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_BlocksConcurrentOperationsForTheSameSession()
    {
        var coordinator = CreateCoordinator();
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None);

        var secondTask = coordinator.AcquireAsync(CreateRequest("alpha", "target-b"), CancellationToken.None);

        await WaitForWaitingCountAsync(coordinator, 1);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("alpha", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task AcquireAsync_BlocksConcurrentOperationsForTheSameTarget()
    {
        var coordinator = CreateCoordinator();
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "shared-target"), CancellationToken.None);

        var secondTask = coordinator.AcquireAsync(CreateRequest("beta", "shared-target"), CancellationToken.None);

        await WaitForWaitingCountAsync(coordinator, 1);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("beta", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task AcquireAsync_AllowsDifferentSessionsAndDifferentTargetsToProceedConcurrently()
    {
        var coordinator = CreateCoordinator();
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None);

        await using var second = await coordinator
            .AcquireAsync(CreateRequest("beta", "target-b"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("beta", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task AcquireAsync_RespectsOptionalGlobalConcurrencyLimit()
    {
        var coordinator = CreateCoordinator(
            enableGlobalCoordination: true,
            maxConcurrentGlobalTargetOperations: 1);
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a", includeGlobalKey: true), CancellationToken.None);

        var secondTask = coordinator.AcquireAsync(CreateRequest("beta", "target-b", includeGlobalKey: true), CancellationToken.None);

        await WaitForWaitingCountAsync(coordinator, 1);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("beta", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task AcquireAsync_DelaysSecondOperationUntilTargetCooldownExpires()
    {
        var coordinator = CreateCoordinator(defaultTargetCooldownMs: 150);
        await using (var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a", cooldown: TimeSpan.FromMilliseconds(150)), CancellationToken.None))
        {
        }

        var stopwatch = Stopwatch.StartNew();
        await using var second = await coordinator.AcquireAsync(
            CreateRequest("beta", "target-a", cooldown: TimeSpan.FromMilliseconds(150)),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100), $"Cooldown elapsed too quickly: {stopwatch.Elapsed}.");
        Assert.Equal("beta", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task Release_AllowsNextWaiterAfterOperationThrows()
    {
        var coordinator = CreateCoordinator();

        try
        {
            await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None);
            throw new InvalidOperationException("simulated failure");
        }
        catch (InvalidOperationException)
        {
        }

        await using var second = await coordinator
            .AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("alpha", second.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task CancelledWait_DoesNotPoisonCoordinator()
    {
        var coordinator = CreateCoordinator();
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var cancelledTask = coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), cts.Token);

        await WaitForWaitingCountAsync(coordinator, 1);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => cancelledTask);

        await first.DisposeAsync();
        await using var third = await coordinator
            .AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("alpha", third.Metadata.SessionId.Value);
    }

    [Fact]
    public async Task Snapshot_ReflectsActiveWaitingAndResourceState()
    {
        var coordinator = CreateCoordinator();
        await using var first = await coordinator.AcquireAsync(CreateRequest("alpha", "target-a"), CancellationToken.None);
        var secondTask = coordinator.AcquireAsync(CreateRequest("beta", "target-a"), CancellationToken.None);

        await WaitForWaitingCountAsync(coordinator, 1);
        var snapshot = await coordinator.GetSnapshotAsync(CancellationToken.None);
        var waitingExecutionId = snapshot.WaitingExecutions.Single().Request.ExecutionId;

        Assert.Single(snapshot.ActiveExecutions);
        Assert.Single(snapshot.WaitingExecutions);
        Assert.Contains(snapshot.Resources, resource => resource.ResourceKey.Scope == ExecutionScope.Target && resource.ActiveExecutionIds.Contains(first.Metadata.ExecutionId));
        Assert.Contains(snapshot.Resources, resource => resource.ResourceKey.Scope == ExecutionScope.Target && resource.WaitingExecutionIds.Contains(waitingExecutionId));

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static IExecutionCoordinator CreateCoordinator(
        int defaultTargetCooldownMs = 0,
        bool enableGlobalCoordination = false,
        int maxConcurrentGlobalTargetOperations = 1) =>
        new InMemoryExecutionCoordinator(
            new SessionHostOptions
            {
                ExecutionCoordination = new ExecutionCoordinationOptions
                {
                    DefaultTargetCooldownMs = defaultTargetCooldownMs,
                    EnableGlobalCoordination = enableGlobalCoordination,
                    MaxConcurrentGlobalTargetOperations = maxConcurrentGlobalTargetOperations,
                    GlobalExclusiveOperationKinds = enableGlobalCoordination ? [ExecutionOperationKind.WorkItem] : []
                },
                Sessions =
                [
                    TestOptionsFactory.Session("alpha"),
                    TestOptionsFactory.Session("beta")
                ]
            },
            new SystemClock(),
            NullLogger<InMemoryExecutionCoordinator>.Instance);

    private static ExecutionRequest CreateRequest(
        string sessionId,
        string targetKey,
        ExecutionOperationKind operationKind = ExecutionOperationKind.WorkItem,
        TimeSpan? cooldown = null,
        bool includeGlobalKey = false)
    {
        var parsedSessionId = new SessionId(sessionId);

        return new ExecutionRequest(
            Guid.NewGuid(),
            parsedSessionId,
            operationKind,
            SessionWorkItemKind.Tick,
            uiCommandKind: null,
            DateTimeOffset.UtcNow,
            new ExecutionResourceSet(
                ExecutionResourceKey.ForSession(parsedSessionId),
                ExecutionResourceKey.ForTarget($"target:{targetKey}"),
                includeGlobalKey ? ExecutionResourceKey.ForGlobal("global:test") : null,
                cooldown ?? TimeSpan.Zero),
            $"Test execution for {sessionId}.");
    }

    private static async Task WaitForWaitingCountAsync(IExecutionCoordinator coordinator, int expectedWaitingCount)
    {
        await TestWait.UntilAsync(
            async () =>
            {
                var snapshot = await coordinator.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
                return snapshot.WaitingExecutions.Count == expectedWaitingCount;
            },
            TimeSpan.FromSeconds(2),
            $"The coordinator did not report {expectedWaitingCount} waiting execution(s) in time.");
    }
}
