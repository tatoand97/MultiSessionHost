using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Persistence;

public sealed class RuntimePersistenceTests
{
    [Fact]
    public void RuntimePersistenceValidation_RequiresJsonBasePath()
    {
        var options = CreatePersistenceOptions(
            " ",
            "persist-validation",
            runtimePersistence: new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = true,
                Mode = RuntimePersistenceMode.JsonFile,
                BasePath = " "
            });

        Assert.False(options.TryValidate(out var error));
        Assert.Equal("RuntimePersistence.BasePath is required when RuntimePersistence.Mode=JsonFile.", error);
    }

    [Fact]
    public void RuntimePersistenceValidation_RejectsInvalidHistoryLimit()
    {
        var options = CreatePersistenceOptions(
            "runtime-state",
            "persist-validation-history",
            runtimePersistence: new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = true,
                Mode = RuntimePersistenceMode.JsonFile,
                BasePath = "runtime-state",
                MaxDecisionHistoryEntries = 0
            });

        Assert.False(options.TryValidate(out var error));
        Assert.Equal("RuntimePersistence.MaxDecisionHistoryEntries must be greater than zero.", error);
    }

    [Fact]
    public void RuntimePersistenceValidation_AllowsDisabledPersistenceWithoutBasePath()
    {
        var options = CreatePersistenceOptions(
            root: string.Empty,
            sessionId: "persist-disabled",
            runtimePersistence: new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = false,
                Mode = RuntimePersistenceMode.JsonFile,
                BasePath = null
            });

        Assert.True(options.TryValidate(out var error), error);
    }

    [Fact]
    public async Task JsonFileBackend_SavesLoadsAndSkipsCorruptFiles()
    {
        var root = CreateTempDirectory();
        var options = CreatePersistenceOptions(root, "persist-backend");
        var backend = CreateBackend(options);
        var sessionId = new SessionId("persist-backend");
        var plan = CreatePlan(sessionId, DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var envelope = new SessionRuntimePersistenceEnvelope(
            options.RuntimePersistence.SchemaVersion,
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:01Z"),
            ActivitySnapshot: null,
            OperationalMemorySnapshot: null,
            OperationalMemoryHistory: [],
            plan,
            [new DecisionPlanHistoryEntry(sessionId, plan.PlannedAtUtc, plan)],
            LatestDecisionExecution: null,
            DecisionExecutionHistory: [],
            Metadata: new Dictionary<string, string>());

        await backend.SaveSessionAsync(envelope, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.runtime.json"), "{ nope", CancellationToken.None);

        var loaded = await backend.LoadSessionAsync(sessionId, CancellationToken.None);
        var all = await backend.LoadAllAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(sessionId, loaded.SessionId);
        Assert.Single(all.Envelopes);
        Assert.Single(all.Errors);
    }

    [Fact]
    public async Task Coordinator_FlushesAndRehydratesBoundedSessionState()
    {
        var root = CreateTempDirectory();
        var options = CreatePersistenceOptions(
            root,
            ["persist-alpha", "persist-beta"],
            runtimePersistence: new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = true,
                Mode = RuntimePersistenceMode.JsonFile,
                BasePath = root,
                MaxDecisionHistoryEntries = 2,
                AutoFlushAfterStateChanges = true
            },
            operationalMemory: new OperationalMemoryOptions { MaxHistoryEntries = 2 },
            decisionExecution: new DecisionExecutionOptions { MaxHistoryEntries = 2 });
        var alpha = new SessionId("persist-alpha");
        var beta = new SessionId("persist-beta");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var first = CreateStores(options, clock);

        await RegisterSessionsAsync(first.Registry, options, clock);
        await SeedStateAsync(first, alpha, clock.UtcNow, entries: 4);
        await SeedStateAsync(first, beta, clock.UtcNow.AddMinutes(1), entries: 1);
        await first.Coordinator.FlushAllAsync(CancellationToken.None);

        var second = CreateStores(options, clock);
        await RegisterSessionsAsync(second.Registry, options, clock);
        await second.Coordinator.RehydrateAsync(CancellationToken.None);

        var alphaPlan = await second.DecisionPlanStore.GetLatestAsync(alpha, CancellationToken.None);
        var alphaPlanHistory = await second.DecisionPlanStore.GetHistoryAsync(alpha, CancellationToken.None);
        var alphaMemoryHistory = await second.MemoryStore.GetHistoryAsync(alpha, CancellationToken.None);
        var alphaExecutionHistory = await second.ExecutionStore.GetHistoryAsync(alpha, CancellationToken.None);
        var alphaActivity = await second.ActivityStore.GetAsync(alpha, CancellationToken.None);
        var betaPlan = await second.DecisionPlanStore.GetLatestAsync(beta, CancellationToken.None);

        Assert.NotNull(alphaPlan);
        Assert.NotNull(betaPlan);
        Assert.Equal(2, alphaPlanHistory.Count);
        Assert.Equal(2, alphaMemoryHistory.Count);
        Assert.Equal(2, alphaExecutionHistory.Count);
        Assert.NotNull(alphaActivity);
        Assert.Equal(4, alphaActivity.History.Count);
    }

    [Fact]
    public async Task Coordinator_IgnoresPersistedSessionsOutsideCurrentConfiguration()
    {
        var root = CreateTempDirectory();
        var originalOptions = CreatePersistenceOptions(root, "persist-present", "persist-removed");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var original = CreateStores(originalOptions, clock);
        await RegisterSessionsAsync(original.Registry, originalOptions, clock);
        await SeedStateAsync(original, new SessionId("persist-removed"), clock.UtcNow, entries: 1);
        await original.Coordinator.FlushAllAsync(CancellationToken.None);

        var currentOptions = CreatePersistenceOptions(root, "persist-present");
        var current = CreateStores(currentOptions, clock);
        await RegisterSessionsAsync(current.Registry, currentOptions, clock);
        await current.Coordinator.RehydrateAsync(CancellationToken.None);

        Assert.Null(await current.DecisionPlanStore.GetLatestAsync(new SessionId("persist-removed"), CancellationToken.None));
    }

    private static async Task SeedStateAsync(TestStores stores, SessionId sessionId, DateTimeOffset now, int entries)
    {
        var activity = SessionActivitySnapshot.CreateBootstrap(sessionId, now);
        for (var index = 0; index < entries; index++)
        {
            var transition = new SessionActivityTransition(
                SessionActivityStateKind.Idle,
                SessionActivityStateKind.MonitoringRisk,
                $"reason-{index}",
                $"Reason {index}",
                now.AddSeconds(index),
                new Dictionary<string, string>());
            activity = InMemorySessionActivityStateStore.AppendTransition(activity, transition);

            var plan = CreatePlan(sessionId, now.AddSeconds(index));
            await stores.DecisionPlanStore.UpdateAsync(sessionId, plan, CancellationToken.None);
            await stores.MemoryStore.UpsertAsync(
                sessionId,
                SessionOperationalMemorySnapshot.Empty(sessionId, now.AddSeconds(index)),
                [new MemoryObservationRecord($"memory-{index}", sessionId, MemoryObservationCategory.Outcome, $"key-{index}", now.AddSeconds(index), "test", "summary", new Dictionary<string, string>())],
                CancellationToken.None);
            var execution = CreateExecution(sessionId, plan, now.AddSeconds(index));
            await stores.ExecutionStore.UpsertCurrentAsync(sessionId, execution, CancellationToken.None);
            await stores.ExecutionStore.AppendHistoryAsync(sessionId, new DecisionPlanExecutionRecord(sessionId, now.AddSeconds(index), execution), CancellationToken.None);
        }

        await stores.ActivityStore.RestoreAsync(sessionId, activity, CancellationToken.None);
    }

    private static DecisionPlan CreatePlan(SessionId sessionId, DateTimeOffset plannedAt) =>
        new(
            sessionId,
            plannedAt,
            DecisionPlanStatus.Ready,
            [new DecisionDirective($"directive-{plannedAt.Ticks}", DecisionDirectiveKind.Observe, 100, "TestPolicy", null, null, "Observe", new Dictionary<string, string>(), [])],
            [],
            new PolicyExecutionSummary(["TestPolicy"], ["TestPolicy"], [], [], 1, 1, new Dictionary<string, int>()),
            []);

    private static DecisionPlanExecutionResult CreateExecution(SessionId sessionId, DecisionPlan plan, DateTimeOffset now) =>
        new(
            sessionId,
            $"fingerprint-{now.Ticks}",
            now,
            now,
            now,
            DecisionPlanExecutionStatus.Succeeded,
            WasAutoExecuted: false,
            [],
            new DecisionPlanExecutionSummary(0, 0, 0, 0, 0, 0, 0, 0, [], [], []),
            DeferredUntilUtc: null,
            FailureReason: null,
            Warnings: [],
            Metadata: new Dictionary<string, string>());

    private static async Task RegisterSessionsAsync(InMemorySessionRegistry registry, SessionHostOptions options, IClock clock)
    {
        foreach (var definition in options.ToSessionDefinitions())
        {
            await registry.RegisterAsync(definition, CancellationToken.None);
        }
    }

    private static TestStores CreateStores(SessionHostOptions options, IClock clock)
    {
        var registry = new InMemorySessionRegistry();
        var activityStore = new InMemorySessionActivityStateStore();
        var memoryStore = new InMemorySessionOperationalMemoryStore(options);
        var planStore = new InMemorySessionDecisionPlanStore(options);
        var executionStore = new InMemorySessionDecisionPlanExecutionStore(options);
        var backend = CreateBackend(options);
        var coordinator = new RuntimePersistenceCoordinator(
            options,
            backend,
            registry,
            clock,
            activityStore,
            memoryStore,
            planStore,
            executionStore,
            NullLogger<RuntimePersistenceCoordinator>.Instance);

        return new TestStores(registry, activityStore, memoryStore, planStore, executionStore, coordinator);
    }

    private static JsonFileRuntimePersistenceBackend CreateBackend(SessionHostOptions options) =>
        new(
            options,
            new TestHostEnvironment { ContentRootPath = Directory.GetCurrentDirectory() },
            NullLogger<JsonFileRuntimePersistenceBackend>.Instance);

    private static SessionHostOptions CreatePersistenceOptions(string root, params string[] sessionIds) =>
        CreatePersistenceOptions(root, sessionIds, runtimePersistence: null, operationalMemory: null, decisionExecution: null);

    private static SessionHostOptions CreatePersistenceOptions(
        string root,
        string sessionId,
        RuntimePersistenceOptions? runtimePersistence = null,
        OperationalMemoryOptions? operationalMemory = null,
        DecisionExecutionOptions? decisionExecution = null) =>
        CreatePersistenceOptions(root, [sessionId], runtimePersistence, operationalMemory, decisionExecution);

    private static SessionHostOptions CreatePersistenceOptions(
        string root,
        IReadOnlyList<string> sessionIds,
        RuntimePersistenceOptions? runtimePersistence = null,
        OperationalMemoryOptions? operationalMemory = null,
        DecisionExecutionOptions? decisionExecution = null) =>
        new()
        {
            MaxGlobalParallelSessions = Math.Max(1, sessionIds.Count),
            SchedulerIntervalMs = 50,
            HealthLogIntervalMs = 1_000,
            RuntimePersistence = runtimePersistence ?? new RuntimePersistenceOptions
            {
                EnableRuntimePersistence = true,
                Mode = RuntimePersistenceMode.JsonFile,
                BasePath = root,
                AutoFlushAfterStateChanges = true,
                FailOnPersistenceErrors = false
            },
            OperationalMemory = operationalMemory ?? new OperationalMemoryOptions(),
            DecisionExecution = decisionExecution ?? new DecisionExecutionOptions(),
            Sessions = sessionIds.Select(id => TestOptionsFactory.Session(id)).ToArray()
        };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MultiSessionHost.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record TestStores(
        InMemorySessionRegistry Registry,
        InMemorySessionActivityStateStore ActivityStore,
        InMemorySessionOperationalMemoryStore MemoryStore,
        InMemorySessionDecisionPlanStore DecisionPlanStore,
        InMemorySessionDecisionPlanExecutionStore ExecutionStore,
        RuntimePersistenceCoordinator Coordinator);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "MultiSessionHost.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
