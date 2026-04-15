using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Drivers;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class DesktopTargetAdapterSystemTests
{
    [Fact]
    public void Registry_ResolvesTheConfiguredAdapterForEachKind()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")));
        services.AddDesktopSessionServices();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IDesktopTargetAdapterRegistry>();

        Assert.IsType<SelfHostedHttpDesktopTargetAdapter>(registry.Resolve(DesktopTargetKind.SelfHostedHttpDesktop));
        Assert.IsType<DesktopTestAppTargetAdapter>(registry.Resolve(DesktopTargetKind.DesktopTestApp));
    }

    [Fact]
    public async Task DesktopTargetSessionDriver_UsesTheAdapterResolvedFromTargetKind()
    {
        var sessionId = new SessionId("alpha");
        var definition = TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")).ToSessionDefinitions().Single();
        var snapshot = new SessionSnapshot(
            definition,
            SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with { DesiredStatus = SessionStatus.Running },
            PendingWorkItems: 0);
        var target = new DesktopSessionTarget(
            sessionId,
            "spy-profile",
            DesktopTargetKind.SelfHostedHttpDesktop,
            DesktopSessionMatchingMode.CommandLine,
            "SpyProcess",
            WindowTitleFragment: null,
            CommandLineFragment: "--session-id alpha",
            BaseAddress: new Uri("http://127.0.0.1:7100/", UriKind.Absolute),
            Metadata: new Dictionary<string, string?>());
        var context = new ResolvedDesktopTargetContext(
            sessionId,
            new DesktopTargetProfile(
                "spy-profile",
                DesktopTargetKind.SelfHostedHttpDesktop,
                "SpyProcess",
                null,
                "--session-id {SessionId}",
                "http://127.0.0.1:{Port}/",
                DesktopSessionMatchingMode.CommandLine,
                new Dictionary<string, string?>(),
                SupportsUiSnapshots: true,
                SupportsStateEndpoint: false),
            new SessionTargetBinding(
                sessionId,
                "spy-profile",
                new Dictionary<string, string> { ["Port"] = "7100" },
                Overrides: null),
            target,
            new Dictionary<string, string>
            {
                ["SessionId"] = sessionId.Value,
                ["Port"] = "7100"
            });
        var attachment = new DesktopSessionAttachment(
            sessionId,
            target,
            new DesktopProcessInfo(100, "SpyProcess", "--session-id alpha", 42),
            new DesktopWindowInfo(42, 100, "Spy", true),
            new Uri("http://127.0.0.1:7100/", UriKind.Absolute),
            DateTimeOffset.UtcNow);
        var uiStateStore = new InMemorySessionUiStateStore();
        await uiStateStore.InitializeAsync(SessionUiState.Create(sessionId), CancellationToken.None);

        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = false,
            Sessions = [TestOptionsFactory.Session("alpha")]
        };
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var adapter = new SpyDesktopTargetAdapter(DesktopTargetKind.SelfHostedHttpDesktop);
        var attachmentRuntime = new StubSessionAttachmentRuntime(attachment);
        var attachmentOperations = new StubSessionAttachmentOperations(attachment);
        var driver = new DesktopTargetSessionDriver(
            attachmentRuntime,
            attachmentOperations,
            new InMemoryExecutionCoordinator(options, clock, NullLogger<InMemoryExecutionCoordinator>.Instance),
            new DefaultExecutionResourceResolver(options, clock),
            new StubDesktopTargetProfileResolver(context),
            new DesktopTargetAdapterRegistry([adapter]),
            new StubSessionUiRefreshService(),
            uiStateStore);

        await driver.ExecuteWorkItemAsync(
            snapshot,
            SessionWorkItem.Create(sessionId, SessionWorkItemKind.Tick, DateTimeOffset.UtcNow, "adapter dispatch test"),
            CancellationToken.None);

        Assert.Equal(0, attachmentRuntime.EnsureCalls);
        Assert.Equal(1, attachmentOperations.EnsureCalls);
        Assert.Equal(0, adapter.ValidateCalls);
        Assert.Equal(0, adapter.AttachCalls);
        Assert.Equal(1, adapter.ExecuteCalls);
        Assert.Equal(SessionWorkItemKind.Tick, adapter.LastWorkItem!.Kind);
    }

    private sealed class SpyDesktopTargetAdapter : IDesktopTargetAdapter
    {
        public SpyDesktopTargetAdapter(DesktopTargetKind kind)
        {
            Kind = kind;
        }

        public DesktopTargetKind Kind { get; }

        public int AttachCalls { get; private set; }

        public int ValidateCalls { get; private set; }

        public int ExecuteCalls { get; private set; }

        public SessionWorkItem? LastWorkItem { get; private set; }

        public Task AttachAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            CancellationToken cancellationToken)
        {
            AttachCalls++;
            return Task.CompletedTask;
        }

        public Task DetachAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment? attachment,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ValidateAttachmentAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            CancellationToken cancellationToken)
        {
            ValidateCalls++;
            return Task.CompletedTask;
        }

        public Task ExecuteWorkItemAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            SessionWorkItem workItem,
            CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            LastWorkItem = workItem;
            return Task.CompletedTask;
        }

        public Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubSessionAttachmentRuntime : ISessionAttachmentRuntime
    {
        private readonly DesktopSessionAttachment _attachment;

        public StubSessionAttachmentRuntime(DesktopSessionAttachment attachment)
        {
            _attachment = attachment;
        }

        public int EnsureCalls { get; private set; }

        public Task<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<DesktopSessionAttachment?>(_attachment);

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(_attachment);
        }

        public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubSessionAttachmentOperations : ISessionAttachmentOperations
    {
        private readonly DesktopSessionAttachment _attachment;

        public StubSessionAttachmentOperations(DesktopSessionAttachment attachment)
        {
            _attachment = attachment;
        }

        public int EnsureCalls { get; private set; }

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(_attachment);
        }

        public Task<bool> InvalidateAsync(
            SessionId sessionId,
            DesktopSessionAttachment currentAttachment,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubDesktopTargetProfileResolver : IDesktopTargetProfileResolver
    {
        private readonly ResolvedDesktopTargetContext _context;

        public StubDesktopTargetProfileResolver(ResolvedDesktopTargetContext context)
        {
            _context = context;
        }

        public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => [_context.Profile];

        public DesktopTargetProfile? TryGetProfile(string profileName) =>
            string.Equals(profileName, _context.Profile.ProfileName, StringComparison.OrdinalIgnoreCase) ? _context.Profile : null;

        public SessionTargetBinding? TryGetBinding(SessionId sessionId) =>
            sessionId == _context.SessionId ? _context.Binding : null;

        public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot) => _context;
    }

    private sealed class StubUiTreeNormalizerResolver : IUiTreeNormalizerResolver
    {
        private sealed class StubUiTreeNormalizer : IUiTreeNormalizer
        {
            public UiTree Normalize(UiSnapshotMetadata metadata, System.Text.Json.JsonElement snapshotRoot) =>
                new(metadata, new UiNode(new UiNodeId("root"), "Root", null, null, null, true, true, false, [], []));
        }

        private readonly IUiTreeNormalizer _normalizer = new StubUiTreeNormalizer();

        public IUiTreeNormalizer Resolve(ResolvedDesktopTargetContext context) => _normalizer;
    }

    private sealed class StubUiStateProjector : IUiStateProjector
    {
        public UiTreeDiff Project(UiTree? previousTree, UiTree currentTree) => new([], [], []);
    }

    private sealed class StubWorkItemPlannerResolver : IWorkItemPlannerResolver
    {
        private sealed class StubPlanner : IWorkItemPlanner
        {
            public IReadOnlyList<PlannedUiWorkItem> Plan(UiTree tree) => [];
        }

        private readonly IWorkItemPlanner _planner = new StubPlanner();

        public IWorkItemPlanner Resolve(ResolvedDesktopTargetContext context) => _planner;
    }

    private sealed class StubSessionUiRefreshService : ISessionUiRefreshService
    {
        public Task<SessionUiState> CaptureAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SessionUiState> ProjectAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment? attachment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SessionUiState> RefreshAsync(
            SessionSnapshot snapshot,
            ResolvedDesktopTargetContext context,
            DesktopSessionAttachment attachment,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
