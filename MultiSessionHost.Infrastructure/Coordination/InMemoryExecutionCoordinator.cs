using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Coordination;

public sealed class InMemoryExecutionCoordinator : IExecutionCoordinator
{
    private readonly ExecutionCoordinationOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<InMemoryExecutionCoordinator> _logger;
    private readonly object _gate = new();
    private readonly List<PendingExecution> _waiting = [];
    private readonly Dictionary<Guid, ActiveExecutionState> _active = [];
    private readonly Dictionary<ExecutionResourceKey, ResourceState> _resourceStates = [];
    private readonly Dictionary<ExecutionScope, long> _contentionHits = new()
    {
        [ExecutionScope.Session] = 0,
        [ExecutionScope.Target] = 0,
        [ExecutionScope.Global] = 0
    };

    private long _totalAcquisitions;
    private long _totalWaitTicks;
    private long _cooldownHitCount;

    public InMemoryExecutionCoordinator(
        SessionHostOptions options,
        IClock clock,
        ILogger<InMemoryExecutionCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.ExecutionCoordination;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IExecutionLease> AcquireAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pending = new PendingExecution(request);
        List<GrantedExecution> grantedExecutions;
        ExecutionWaitInfo? waitInfo = null;

        lock (_gate)
        {
            _waiting.Add(pending);
            grantedExecutions = TryGrantWaitersNoLock(_clock.UtcNow);

            if (pending.State == PendingExecutionState.Waiting &&
                pending.TryRegisterFirstWait(out var blockingKeys, out var cooldownHit))
            {
                waitInfo = new ExecutionWaitInfo(request, blockingKeys, cooldownHit);
                RegisterContentionNoLock(blockingKeys, cooldownHit);
            }
        }

        CompleteGrantedExecutions(grantedExecutions);
        LogWait(waitInfo);

        using var registration = cancellationToken.Register(
            static state =>
            {
                var callbackState = (CancellationState)state!;
                callbackState.Coordinator.CancelPending(callbackState.Pending, callbackState.CancellationToken);
            },
            new CancellationState(this, pending, cancellationToken));

        var metadata = await pending.Completion.Task.ConfigureAwait(false);
        return new ExecutionLease(this, metadata);
    }

    public Task<ExecutionCoordinationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var now = _clock.UtcNow;
            var waitingByResource = BuildWaitingByResourceNoLock();
            var allResourceKeys = _resourceStates.Keys
                .Concat(waitingByResource.Keys)
                .Distinct()
                .OrderBy(static key => key.Scope)
                .ThenBy(static key => key.Value, StringComparer.Ordinal)
                .ToArray();
            var resources = allResourceKeys
                .Select(
                    key =>
                    {
                        _resourceStates.TryGetValue(key, out var state);
                        waitingByResource.TryGetValue(key, out var waitingIds);

                        var lastCompletedAtUtc = state?.LastCompletedAtUtc;
                        var cooldownUntilUtc = key.Scope == ExecutionScope.Target && state is not null && state.TargetCooldown > TimeSpan.Zero
                            ? state.LastCompletedAtUtc?.Add(state.TargetCooldown)
                            : null;

                        return new ExecutionResourceState(
                            key,
                            Capacity: GetCapacity(key.Scope),
                            ActiveExecutionIds: state?.ActiveExecutionIds.OrderBy(static id => id).ToArray() ?? [],
                            WaitingExecutionIds: waitingIds?.OrderBy(static id => id).ToArray() ?? [],
                            LastCompletedAtUtc: lastCompletedAtUtc,
                            CooldownUntilUtc: cooldownUntilUtc);
                    })
                .ToArray();
            var active = _active.Values
                .OrderBy(static value => value.Metadata.AcquiredAtUtc)
                .ThenBy(static value => value.Metadata.ExecutionId)
                .Select(value => new ActiveExecutionEntry(value.Metadata, now - value.Metadata.AcquiredAtUtc))
                .ToArray();
            var waiting = _waiting
                .Where(static pending => pending.State == PendingExecutionState.Waiting)
                .OrderBy(static pending => pending.Request.RequestedAtUtc)
                .ThenBy(static pending => pending.Request.ExecutionId)
                .Select(
                    pending => new WaitingExecutionEntry(
                        pending.Request,
                        now - pending.Request.RequestedAtUtc,
                        pending.BlockingResourceKeys.ToArray()))
                .ToArray();
            var acquisitions = Interlocked.Read(ref _totalAcquisitions);
            var averageWaitMs = acquisitions == 0
                ? 0
                : TimeSpan.FromTicks(Interlocked.Read(ref _totalWaitTicks) / acquisitions).TotalMilliseconds;
            var contentionByScope = _contentionHits
                .OrderBy(static pair => pair.Key)
                .Select(pair => new ExecutionContentionStat(pair.Key, pair.Value))
                .ToArray();

            return Task.FromResult(
                new ExecutionCoordinationSnapshot(
                    now,
                    active,
                    waiting,
                    resources,
                    acquisitions,
                    averageWaitMs,
                    Interlocked.Read(ref _cooldownHitCount),
                    contentionByScope));
        }
    }

    private void CancelPending(PendingExecution pending, CancellationToken cancellationToken)
    {
        List<GrantedExecution>? grantedExecutions = null;
        var shouldCancel = false;

        lock (_gate)
        {
            if (pending.State != PendingExecutionState.Waiting)
            {
                return;
            }

            shouldCancel = _waiting.Remove(pending);

            if (!shouldCancel)
            {
                return;
            }

            pending.State = PendingExecutionState.Cancelled;
            grantedExecutions = TryGrantWaitersNoLock(_clock.UtcNow);
        }

        pending.Completion.TrySetCanceled(cancellationToken);

        if (grantedExecutions is not null)
        {
            CompleteGrantedExecutions(grantedExecutions);
        }
    }

    private async ValueTask ReleaseAsync(ExecutionLeaseMetadata metadata)
    {
        List<GrantedExecution>? grantedExecutions = null;

        lock (_gate)
        {
            if (!_active.Remove(metadata.ExecutionId, out var activeState))
            {
                return;
            }

            foreach (var key in GetTrackedResourceKeys(activeState.Metadata.Request()))
            {
                if (!_resourceStates.TryGetValue(key, out var resourceState))
                {
                    continue;
                }

                resourceState.ActiveExecutionIds.Remove(metadata.ExecutionId);
                resourceState.LastCompletedAtUtc = _clock.UtcNow;

                if (resourceState.ActiveExecutionIds.Count == 0 &&
                    resourceState.LastCompletedAtUtc is null &&
                    resourceState.TargetCooldown == TimeSpan.Zero)
                {
                    _resourceStates.Remove(key);
                }
            }

            grantedExecutions = TryGrantWaitersNoLock(_clock.UtcNow);
        }

        _logger.LogInformation(
            "Execution lease released. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
            metadata.ExecutionId,
            metadata.SessionId,
            metadata.OperationKind);

        if (grantedExecutions is not null)
        {
            CompleteGrantedExecutions(grantedExecutions);
        }

        await ValueTask.CompletedTask;
    }

    private List<GrantedExecution> TryGrantWaitersNoLock(DateTimeOffset now)
    {
        var granted = new List<GrantedExecution>();
        var reservedKeys = new HashSet<ExecutionResourceKey>();

        foreach (var pending in _waiting)
        {
            if (pending.State != PendingExecutionState.Waiting)
            {
                continue;
            }

            var fairnessKeys = GetFairnessKeys(pending.Request);
            var earlierBlockingKeys = fairnessKeys.Where(reservedKeys.Contains).ToArray();

            if (earlierBlockingKeys.Length > 0)
            {
                pending.SetBlocking(earlierBlockingKeys);
                reservedKeys.UnionWith(fairnessKeys);
                continue;
            }

            var blockResult = GetResourceBlockResultNoLock(pending.Request, now);

            if (blockResult.BlockingResourceKeys.Count > 0)
            {
                pending.SetBlocking(blockResult.BlockingResourceKeys);
                pending.SetCooldownHit(blockResult.CooldownHit);
                if (blockResult.CooldownUntilUtc is not null)
                {
                    ScheduleCooldownWake(blockResult.CooldownUntilUtc.Value);
                }

                reservedKeys.UnionWith(fairnessKeys);
                continue;
            }

            var metadata = ActivateNoLock(pending.Request, now);
            pending.State = PendingExecutionState.Granted;
            granted.Add(new GrantedExecution(pending, metadata));
        }

        _waiting.RemoveAll(static pending => pending.State != PendingExecutionState.Waiting);
        return granted;
    }

    private ExecutionLeaseMetadata ActivateNoLock(ExecutionRequest request, DateTimeOffset now)
    {
        var waitDuration = now - request.RequestedAtUtc;
        var metadata = new ExecutionLeaseMetadata(
            request.ExecutionId,
            request.SessionId,
            request.OperationKind,
            request.WorkItemKind,
            request.UiCommandKind,
            request.RequestedAtUtc,
            now,
            waitDuration,
            request.ResourceSet,
            request.Description);

        _active[request.ExecutionId] = new ActiveExecutionState(metadata);

        foreach (var key in GetTrackedResourceKeys(request))
        {
            if (!_resourceStates.TryGetValue(key, out var resourceState))
            {
                resourceState = new ResourceState(key);
                _resourceStates[key] = resourceState;
            }

            resourceState.ActiveExecutionIds.Add(request.ExecutionId);

            if (key.Scope == ExecutionScope.Target)
            {
                resourceState.TargetCooldown = request.ResourceSet.TargetCooldown;
            }
        }

        Interlocked.Increment(ref _totalAcquisitions);
        Interlocked.Add(ref _totalWaitTicks, waitDuration.Ticks);

        return metadata;
    }

    private ResourceBlockResult GetResourceBlockResultNoLock(ExecutionRequest request, DateTimeOffset now)
    {
        var blockingKeys = new List<ExecutionResourceKey>(capacity: 3);
        var cooldownHit = false;
        DateTimeOffset? cooldownUntilUtc = null;

        if (ShouldEnforceScope(ExecutionScope.Session, request.OperationKind) &&
            IsCapacityReachedNoLock(request.ResourceSet.SessionResourceKey, GetCapacity(ExecutionScope.Session)))
        {
            blockingKeys.Add(request.ResourceSet.SessionResourceKey);
        }

        if (request.ResourceSet.TargetResourceKey is not null)
        {
            if (ShouldEnforceScope(ExecutionScope.Target, request.OperationKind) &&
                IsCapacityReachedNoLock(request.ResourceSet.TargetResourceKey, GetCapacity(ExecutionScope.Target)))
            {
                blockingKeys.Add(request.ResourceSet.TargetResourceKey);
            }

            if (request.ResourceSet.TargetCooldown > TimeSpan.Zero &&
                _resourceStates.TryGetValue(request.ResourceSet.TargetResourceKey, out var targetState) &&
                targetState.LastCompletedAtUtc is { } lastCompletedAtUtc &&
                lastCompletedAtUtc.Add(request.ResourceSet.TargetCooldown) is { } targetCooldownUntilUtc &&
                targetCooldownUntilUtc > now)
            {
                cooldownHit = true;
                cooldownUntilUtc = targetCooldownUntilUtc;

                if (!blockingKeys.Contains(request.ResourceSet.TargetResourceKey))
                {
                    blockingKeys.Add(request.ResourceSet.TargetResourceKey);
                }
            }
        }

        if (request.ResourceSet.GlobalResourceKey is not null &&
            ShouldEnforceScope(ExecutionScope.Global, request.OperationKind) &&
            IsCapacityReachedNoLock(request.ResourceSet.GlobalResourceKey, GetCapacity(ExecutionScope.Global)))
        {
            blockingKeys.Add(request.ResourceSet.GlobalResourceKey);
        }

        return new ResourceBlockResult(blockingKeys, cooldownHit, cooldownUntilUtc);
    }

    private bool IsCapacityReachedNoLock(ExecutionResourceKey key, int capacity)
    {
        if (!_resourceStates.TryGetValue(key, out var state))
        {
            return false;
        }

        return state.ActiveExecutionIds.Count >= capacity;
    }

    private IReadOnlyList<ExecutionResourceKey> GetTrackedResourceKeys(ExecutionRequest request)
    {
        var keys = new List<ExecutionResourceKey>(capacity: 3);

        if (ShouldEnforceScope(ExecutionScope.Session, request.OperationKind))
        {
            keys.Add(request.ResourceSet.SessionResourceKey);
        }

        if (request.ResourceSet.TargetResourceKey is not null &&
            (ShouldEnforceScope(ExecutionScope.Target, request.OperationKind) || request.ResourceSet.TargetCooldown > TimeSpan.Zero))
        {
            keys.Add(request.ResourceSet.TargetResourceKey);
        }

        if (request.ResourceSet.GlobalResourceKey is not null &&
            ShouldEnforceScope(ExecutionScope.Global, request.OperationKind))
        {
            keys.Add(request.ResourceSet.GlobalResourceKey);
        }

        return keys;
    }

    private IReadOnlyList<ExecutionResourceKey> GetFairnessKeys(ExecutionRequest request) => GetTrackedResourceKeys(request);

    private bool ShouldEnforceScope(ExecutionScope scope, ExecutionOperationKind operationKind) =>
        scope switch
        {
            ExecutionScope.Session => _options.SessionExclusiveOperationKinds.Contains(operationKind),
            ExecutionScope.Target => _options.EnableTargetCoordination && _options.TargetExclusiveOperationKinds.Contains(operationKind),
            ExecutionScope.Global => _options.EnableGlobalCoordination && _options.GlobalExclusiveOperationKinds.Contains(operationKind),
            _ => false
        };

    private int GetCapacity(ExecutionScope scope) =>
        scope switch
        {
            ExecutionScope.Global => _options.MaxConcurrentGlobalTargetOperations,
            _ => 1
        };

    private Dictionary<ExecutionResourceKey, List<Guid>> BuildWaitingByResourceNoLock()
    {
        var waitingByResource = new Dictionary<ExecutionResourceKey, List<Guid>>();

        foreach (var pending in _waiting.Where(static pending => pending.State == PendingExecutionState.Waiting))
        {
            foreach (var key in GetTrackedResourceKeys(pending.Request))
            {
                if (!waitingByResource.TryGetValue(key, out var executionIds))
                {
                    executionIds = [];
                    waitingByResource[key] = executionIds;
                }

                executionIds.Add(pending.Request.ExecutionId);
            }
        }

        return waitingByResource;
    }

    private void RegisterContentionNoLock(IReadOnlyList<ExecutionResourceKey> blockingKeys, bool cooldownHit)
    {
        foreach (var scope in blockingKeys.Select(static key => key.Scope).Distinct())
        {
            _contentionHits[scope] = _contentionHits.GetValueOrDefault(scope) + 1;
        }

        if (cooldownHit)
        {
            Interlocked.Increment(ref _cooldownHitCount);
        }
    }

    private void ScheduleCooldownWake(DateTimeOffset cooldownUntilUtc)
    {
        var delay = cooldownUntilUtc - _clock.UtcNow;

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        _ = WakeAfterCooldownAsync(delay);
    }

    private async Task WakeAfterCooldownAsync(TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);

        List<GrantedExecution> grantedExecutions;

        lock (_gate)
        {
            grantedExecutions = TryGrantWaitersNoLock(_clock.UtcNow);
        }

        CompleteGrantedExecutions(grantedExecutions);
    }

    private void CompleteGrantedExecutions(IEnumerable<GrantedExecution> grantedExecutions)
    {
        foreach (var grantedExecution in grantedExecutions)
        {
            var metadata = grantedExecution.Metadata;
            grantedExecution.Pending.Completion.TrySetResult(metadata);

            if (metadata.WaitDuration > TimeSpan.Zero)
            {
                if (metadata.WaitDuration >= TimeSpan.FromMilliseconds(_options.WaitWarningThresholdMs))
                {
                    _logger.LogWarning(
                        "Execution waited {WaitDurationMs} ms before acquiring a lease. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                        metadata.WaitDuration.TotalMilliseconds,
                        metadata.ExecutionId,
                        metadata.SessionId,
                        metadata.OperationKind);
                }
                else
                {
                    _logger.LogInformation(
                        "Execution acquired after waiting {WaitDurationMs} ms. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                        metadata.WaitDuration.TotalMilliseconds,
                        metadata.ExecutionId,
                        metadata.SessionId,
                        metadata.OperationKind);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Execution lease acquired immediately. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                    metadata.ExecutionId,
                    metadata.SessionId,
                    metadata.OperationKind);
            }
        }
    }

    private void LogWait(ExecutionWaitInfo? waitInfo)
    {
        if (waitInfo is null)
        {
            return;
        }

        if (waitInfo.BlockingKeys.Any(static key => key.Scope == ExecutionScope.Session))
        {
            _logger.LogInformation(
                "Execution is waiting on the session resource. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                waitInfo.Request.ExecutionId,
                waitInfo.Request.SessionId,
                waitInfo.Request.OperationKind);
        }

        if (waitInfo.BlockingKeys.Any(static key => key.Scope == ExecutionScope.Target))
        {
            _logger.LogInformation(
                "Execution is waiting on the target resource. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                waitInfo.Request.ExecutionId,
                waitInfo.Request.SessionId,
                waitInfo.Request.OperationKind);
        }

        if (waitInfo.BlockingKeys.Any(static key => key.Scope == ExecutionScope.Global))
        {
            _logger.LogInformation(
                "Execution is waiting on the global resource. ExecutionId={ExecutionId} SessionId={SessionId} Operation={OperationKind}",
                waitInfo.Request.ExecutionId,
                waitInfo.Request.SessionId,
                waitInfo.Request.OperationKind);
        }

        if (waitInfo.CooldownHit &&
            waitInfo.Request.ResourceSet.TargetResourceKey is not null)
        {
            _logger.LogInformation(
                "Execution is delayed by target cooldown. ExecutionId={ExecutionId} SessionId={SessionId} TargetResourceKey={TargetResourceKey}",
                waitInfo.Request.ExecutionId,
                waitInfo.Request.SessionId,
                waitInfo.Request.ResourceSet.TargetResourceKey);
        }
    }

    private sealed class ExecutionLease : IExecutionLease
    {
        private readonly InMemoryExecutionCoordinator _coordinator;
        private int _disposed;

        public ExecutionLease(InMemoryExecutionCoordinator coordinator, ExecutionLeaseMetadata metadata)
        {
            _coordinator = coordinator;
            Metadata = metadata;
        }

        public ExecutionLeaseMetadata Metadata { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _coordinator.ReleaseAsync(Metadata).ConfigureAwait(false);
        }
    }

    private sealed class PendingExecution
    {
        public PendingExecution(ExecutionRequest request)
        {
            Request = request;
            Completion = new TaskCompletionSource<ExecutionLeaseMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ExecutionRequest Request { get; }

        public TaskCompletionSource<ExecutionLeaseMetadata> Completion { get; }

        public PendingExecutionState State { get; set; } = PendingExecutionState.Waiting;

        public IReadOnlyList<ExecutionResourceKey> BlockingResourceKeys { get; private set; } = [];

        private bool HasRegisteredFirstWait { get; set; }

        private bool CooldownHit { get; set; }

        public void SetBlocking(IReadOnlyList<ExecutionResourceKey> blockingResourceKeys)
        {
            BlockingResourceKeys = blockingResourceKeys;
        }

        public bool TryRegisterFirstWait(out IReadOnlyList<ExecutionResourceKey> blockingResourceKeys, out bool cooldownHit)
        {
            if (HasRegisteredFirstWait)
            {
                blockingResourceKeys = BlockingResourceKeys;
                cooldownHit = CooldownHit;
                return false;
            }

            HasRegisteredFirstWait = true;
            blockingResourceKeys = BlockingResourceKeys;
            cooldownHit = CooldownHit;
            return true;
        }

        public void SetCooldownHit(bool cooldownHit)
        {
            CooldownHit = cooldownHit;
        }
    }

    private sealed class ActiveExecutionState
    {
        public ActiveExecutionState(ExecutionLeaseMetadata metadata)
        {
            Metadata = metadata;
        }

        public ExecutionLeaseMetadata Metadata { get; }
    }

    private sealed class ResourceState
    {
        public ResourceState(ExecutionResourceKey key)
        {
            Key = key;
        }

        public ExecutionResourceKey Key { get; }

        public HashSet<Guid> ActiveExecutionIds { get; } = [];

        public DateTimeOffset? LastCompletedAtUtc { get; set; }

        public TimeSpan TargetCooldown { get; set; }
    }

    private sealed record GrantedExecution(
        PendingExecution Pending,
        ExecutionLeaseMetadata Metadata);

    private sealed record ResourceBlockResult(
        IReadOnlyList<ExecutionResourceKey> BlockingResourceKeys,
        bool CooldownHit,
        DateTimeOffset? CooldownUntilUtc);

    private sealed record ExecutionWaitInfo(
        ExecutionRequest Request,
        IReadOnlyList<ExecutionResourceKey> BlockingKeys,
        bool CooldownHit);

    private sealed record CancellationState(
        InMemoryExecutionCoordinator Coordinator,
        PendingExecution Pending,
        CancellationToken CancellationToken);

    private enum PendingExecutionState
    {
        Waiting = 0,
        Granted = 1,
        Cancelled = 2
    }
}

internal static class ExecutionLeaseMetadataExtensions
{
    public static ExecutionRequest Request(this ExecutionLeaseMetadata metadata) =>
        new(
            metadata.ExecutionId,
            metadata.SessionId,
            metadata.OperationKind,
            metadata.WorkItemKind,
            metadata.UiCommandKind,
            metadata.RequestedAtUtc,
            metadata.ResourceSet,
            metadata.Description);
}
