using System.Text.Json;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class AttachmentRecoveryTests
{
    [Fact]
    public async Task EnsureAttachedAsync_ReattachesWhenTargetChanges_AndClearsRecoveryPressure()
    {
        var sessionId = new SessionId("recovery-attach");
        var now = DateTimeOffset.UtcNow;
        var clock = new FakeClock(now);
        var registry = new StubSessionRegistry(sessionId);
        var stateStore = new StubSessionStateStore(sessionId, clock);
        var attachedSessionStore = new InMemoryAttachedSessionStore();
        var recoveryStore = new InMemorySessionRecoveryStateStore(TestOptionsFactory.Create(TestOptionsFactory.Session(sessionId.Value)), clock);
        var adapterRegistry = new StubAdapterRegistry(new SpyDesktopTargetAdapter());
        var targetProfileResolver = new StubTargetProfileResolver();
        var attachmentResolver = new StubAttachmentResolver();
        var observabilityRecorder = new NoOpObservabilityRecorder();
        var operations = new DefaultSessionAttachmentOperations(
            registry,
            stateStore,
            attachmentResolver,
            attachedSessionStore,
            targetProfileResolver,
            adapterRegistry,
            recoveryStore,
            observabilityRecorder);

        var currentAttachment = CreateAttachment(sessionId, "profile-old", "http://127.0.0.1:7000/");
        await attachedSessionStore.SetAsync(currentAttachment, CancellationToken.None);

        var snapshot = CreateSnapshot(sessionId);
        var resolvedContext = CreateContext(snapshot, "profile-new", "http://127.0.0.1:7001/");

        var attachment = await operations.EnsureAttachedAsync(snapshot, resolvedContext, CancellationToken.None);
        var recoveryState = await recoveryStore.GetAsync(sessionId, CancellationToken.None);

        Assert.Equal("profile-new", attachment.Target.ProfileName);
        Assert.Equal("http://127.0.0.1:7001/", attachment.BaseAddress?.AbsoluteUri);
        Assert.False(recoveryState.IsAttachmentInvalid);
        Assert.False(recoveryState.IsTargetQuarantined);
        Assert.Equal(SessionRecoveryStatus.Healthy, recoveryState.RecoveryStatus);
    }

    private static SessionSnapshot CreateSnapshot(SessionId sessionId)
    {
        var definition = new SessionDefinition(sessionId, "recovery-attach", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), []);
        var state = SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with
        {
            DesiredStatus = SessionStatus.Running,
            CurrentStatus = SessionStatus.Running,
            ObservedStatus = SessionStatus.Running
        };

        return new SessionSnapshot(definition, state, PendingWorkItems: 0);
    }

    private static DesktopSessionAttachment CreateAttachment(SessionId sessionId, string profileName, string baseAddress) =>
        new(
            sessionId,
            new DesktopSessionTarget(
                sessionId,
                profileName,
                DesktopTargetKind.SelfHostedHttpDesktop,
                DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                "process-old",
                "window-old",
                "cmd-old",
                new Uri(baseAddress, UriKind.Absolute),
                new Dictionary<string, string?>(StringComparer.Ordinal)),
            new DesktopProcessInfo(100, "process-old", "cmd-old", 1),
            new DesktopWindowInfo(1, 100, "window-old", true),
            new Uri(baseAddress, UriKind.Absolute),
            DateTimeOffset.UtcNow);

    private static ResolvedDesktopTargetContext CreateContext(SessionSnapshot snapshot, string profileName, string baseAddress)
    {
        var profile = new DesktopTargetProfile(
            profileName,
            DesktopTargetKind.SelfHostedHttpDesktop,
            "process-new",
            "window-new",
            "cmd-new",
            baseAddress,
            DesktopSessionMatchingMode.WindowTitleAndCommandLine,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: true);
        var binding = new SessionTargetBinding(snapshot.SessionId, profileName, new Dictionary<string, string>(), null);
        var target = new DesktopSessionTarget(
            snapshot.SessionId,
            profileName,
            DesktopTargetKind.SelfHostedHttpDesktop,
            DesktopSessionMatchingMode.WindowTitleAndCommandLine,
            "process-new",
            "window-new",
            "cmd-new",
            new Uri(baseAddress, UriKind.Absolute),
            new Dictionary<string, string?>(StringComparer.Ordinal));

        return new ResolvedDesktopTargetContext(snapshot.SessionId, profile, binding, target, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private sealed class StubSessionRegistry : ISessionRegistry
    {
        private readonly SessionDefinition _definition;

        public StubSessionRegistry(SessionId sessionId)
        {
            _definition = new SessionDefinition(sessionId, "recovery-attach", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), []);
        }

        public ValueTask RegisterAsync(SessionDefinition definition, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public IReadOnlyCollection<SessionDefinition> GetAll() => [_definition];

        public SessionDefinition? GetById(SessionId sessionId) => sessionId == _definition.Id ? _definition : null;
    }

    private sealed class StubSessionStateStore : ISessionStateStore
    {
        private readonly SessionRuntimeState _state;

        public StubSessionStateStore(SessionId sessionId, IClock clock)
        {
            var definition = new SessionDefinition(sessionId, "recovery-attach", true, TimeSpan.FromSeconds(1), TimeSpan.Zero, 1, 3, TimeSpan.FromMilliseconds(100), []);
            _state = SessionRuntimeState.Create(definition, clock.UtcNow) with
            {
                DesiredStatus = SessionStatus.Running,
                CurrentStatus = SessionStatus.Running,
                ObservedStatus = SessionStatus.Running
            };
        }

        public ValueTask InitializeAsync(SessionRuntimeState state, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<SessionRuntimeState?> GetAsync(SessionId sessionId, CancellationToken cancellationToken) => ValueTask.FromResult<SessionRuntimeState?>(_state);

        public IReadOnlyCollection<SessionRuntimeState> GetAll() => [_state];

        public ValueTask<SessionRuntimeState> SetAsync(SessionRuntimeState state, CancellationToken cancellationToken) => ValueTask.FromResult(state);

        public ValueTask<SessionRuntimeState> UpdateAsync(SessionId sessionId, Func<SessionRuntimeState, SessionRuntimeState> update, CancellationToken cancellationToken) => ValueTask.FromResult(update(_state));
    }

    private sealed class StubAttachmentResolver : ISessionAttachmentResolver
    {
        public ValueTask<DesktopSessionAttachment> ResolveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) => ValueTask.FromResult(CreateAttachment(snapshot.SessionId, "profile-new", "http://127.0.0.1:7001/"));
    }

    private sealed class StubTargetProfileResolver : IDesktopTargetProfileResolver
    {
        public IReadOnlyCollection<DesktopTargetProfile> GetProfiles() => [];

        public DesktopTargetProfile? TryGetProfile(string profileName) => null;

        public SessionTargetBinding? TryGetBinding(SessionId sessionId) => null;

        public ResolvedDesktopTargetContext Resolve(SessionSnapshot snapshot) => CreateContext(snapshot, "profile-new", "http://127.0.0.1:7001/");
    }

    private sealed class StubAdapterRegistry : IDesktopTargetAdapterRegistry
    {
        private readonly IDesktopTargetAdapter _adapter;

        public StubAdapterRegistry(IDesktopTargetAdapter adapter)
        {
            _adapter = adapter;
        }

        public IDesktopTargetAdapter Resolve(DesktopTargetKind kind) => _adapter;
    }

    private sealed class SpyDesktopTargetAdapter : IDesktopTargetAdapter
    {
        public DesktopTargetKind Kind => DesktopTargetKind.SelfHostedHttpDesktop;

        public Task AttachAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DetachAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment? attachment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ValidateAttachmentAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExecuteWorkItemAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, SessionWorkItem workItem, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("{}");
            return Task.FromResult(new UiSnapshotEnvelope(snapshot.SessionId.Value, DateTimeOffset.UtcNow, new DesktopProcessInfo(1, "process", null, 1), new DesktopWindowInfo(1, 1, "window", true), document.RootElement.Clone(), new Dictionary<string, string?>(StringComparer.Ordinal)));
        }
    }
}
