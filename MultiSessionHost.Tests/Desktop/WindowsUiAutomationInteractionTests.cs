using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Infrastructure.Coordination;
using MultiSessionHost.Tests.Common;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class WindowsUiAutomationInteractionTests
{
    [Fact]
    public void Services_RegisterNativeInteractionAdapterForWindowsUiAutomationDesktop()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")));
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        services.AddDesktopSessionServices();
        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetServices<IUiInteractionAdapter>()
            .Single(candidate => candidate.Kind == DesktopTargetKind.WindowsUiAutomationDesktop);

        Assert.IsType<WindowsUiAutomationUiInteractionAdapter>(adapter);
    }

    [Fact]
    public async Task Locator_ResolvesProjectedNodeFromLiveIdentity()
    {
        var button = Element("Button", "Start", "startButton");
        var root = Element("Window", "Fixture", "root", children: [button]);
        var action = CreateAction(root, button, UiCommandKind.ClickNode);
        var locator = CreateLocator(root);

        var located = await locator.LocateAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.Same(button, located.Element);
        Assert.True(located.IsExactNodeIdMatch);
        Assert.Equal("node-id", located.MatchStrategy);
    }

    [Fact]
    public async Task Locator_HandlesModestTreeDriftWithStableAutomationIdentity()
    {
        var oldButton = Element("Button", "Start", "startButton");
        var oldRoot = Element("Window", "Fixture", "root", children: [oldButton]);
        var action = CreateAction(oldRoot, oldButton, UiCommandKind.ClickNode);
        var newButton = Element("Button", "Start", "startButton");
        var newRoot = Element("Window", "Fixture", "root", children: [Element("Text", "Inserted", "inserted"), newButton]);
        var locator = CreateLocator(newRoot);

        var located = await locator.LocateAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.Same(newButton, located.Element);
    }

    [Fact]
    public async Task Locator_FailsCleanlyWhenNodeIsMissing()
    {
        var button = Element("Button", "Start", "startButton");
        var oldRoot = Element("Window", "Fixture", "root", children: [button]);
        var action = CreateAction(oldRoot, button, UiCommandKind.ClickNode);
        var locator = CreateLocator(Element("Window", "Fixture", "root"));

        var exception = await Assert.ThrowsAsync<NativeUiAutomationInteractionException>(
            () => locator.LocateAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None));

        Assert.Equal(UiCommandFailureCodes.NativeElementNotFound, exception.FailureCode);
    }

    [Fact]
    public async Task Click_PrefersSemanticToggleBeforeInvoke()
    {
        var toggle = Element("CheckBox", "Enabled", "enabledCheckBox", isSelected: false, canToggle: true, canInvoke: true);
        var root = Element("Window", "Fixture", "root", children: [toggle]);
        var adapter = CreateAdapter(root);
        var action = CreateAction(root, toggle, UiCommandKind.ClickNode, boolValue: true);

        var result = await adapter.ClickAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(toggle.IsSelected);
        Assert.Equal(1, toggle.ToggleCalls);
        Assert.Equal(0, toggle.InvokeCalls);
    }

    [Fact]
    public async Task Invoke_UsesInvokePattern()
    {
        var button = Element("Button", "Start", "startButton", canInvoke: true);
        var root = Element("Window", "Fixture", "root", children: [button]);
        var adapter = CreateAdapter(root);
        var action = CreateAction(root, button, UiCommandKind.InvokeNodeAction, actionName: "default");

        var result = await adapter.InvokeAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, button.InvokeCalls);
    }

    [Fact]
    public async Task SetText_UsesValuePatternAndVerifies()
    {
        var input = Element("TextBox", "Notes", "notesTextBox", value: "old", canSetValue: true);
        var root = Element("Window", "Fixture", "root", children: [input]);
        var adapter = CreateAdapter(root);
        var action = CreateAction(root, input, UiCommandKind.SetText, textValue: "updated");

        var result = await adapter.SetTextAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("updated", input.Value);
        Assert.Contains("ValuePattern", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Toggle_IsIdempotentForDesiredState()
    {
        var toggle = Element("CheckBox", "Enabled", "enabledCheckBox", isSelected: true, canToggle: true);
        var root = Element("Window", "Fixture", "root", children: [toggle]);
        var adapter = CreateAdapter(root);
        var action = CreateAction(root, toggle, UiCommandKind.ToggleNode, boolValue: true);

        var result = await adapter.ToggleAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, toggle.ToggleCalls);
        Assert.True(toggle.IsSelected);
    }

    [Fact]
    public async Task SelectItem_SelectsMatchingDescendant()
    {
        var item = Element("ListItem", "second", "item2", canSelect: true);
        var list = Element("ListBox", "Items", "itemsList", children: [Element("ListItem", "first", "item1"), item]);
        var root = Element("Window", "Fixture", "root", children: [list]);
        var adapter = CreateAdapter(root);
        var action = CreateAction(root, list, UiCommandKind.SelectItem, selectedValue: "second");

        var result = await adapter.SelectItemAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(item.IsSelected);
        Assert.Equal(1, item.SelectCalls);
    }

    [Fact]
    public async Task TargetLoss_ReturnsRecoveryCompatibleFailure()
    {
        var button = Element("Button", "Start", "startButton", canInvoke: true);
        var root = Element("Window", "Fixture", "root", children: [button]);
        var recovery = new InMemorySessionRecoveryStateStore(CreateOptions(), new FakeClock(DateTimeOffset.UtcNow));
        var adapter = CreateAdapter(root, recoveryStore: recovery, windowAvailable: false);
        var action = CreateAction(root, button, UiCommandKind.InvokeNodeAction);

        var result = await adapter.InvokeAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);
        var snapshot = await recovery.GetAsync(new SessionId("alpha"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(UiCommandFailureCodes.NativeTargetLostDuringAction, result.FailureCode);
        Assert.True(snapshot.IsAttachmentInvalid);
    }

    [Fact]
    public async Task Observability_RecordsNativeActionAndAdapterErrors()
    {
        var unsupported = Element("Text", "Label", "label");
        var root = Element("Window", "Fixture", "root", children: [unsupported]);
        var recorder = new RecordingObservabilityRecorder();
        var adapter = CreateAdapter(root, recorder: recorder);
        var action = CreateAction(root, unsupported, UiCommandKind.InvokeNodeAction);

        var result = await adapter.InvokeAsync(CreateContext(), CreateAttachment(), action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("native.action.locate.started", recorder.Stages);
        Assert.Contains("native.action.failed", recorder.Stages);
        Assert.Contains("native.action.invoke", recorder.AdapterErrors);
    }

    [Fact]
    public async Task Cancellation_IsRespectedDuringLocation()
    {
        var button = Element("Button", "Start", "startButton");
        var root = Element("Window", "Fixture", "root", children: [button]);
        var locator = CreateLocator(root);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => locator.LocateAsync(CreateContext(), CreateAttachment(), CreateAction(root, button, UiCommandKind.ClickNode), cancellation.Token));
    }

    [Fact]
    public async Task UiCommandExecutor_DispatchesWindowsUiAutomationCommandAndRefreshesAfterSuccess()
    {
        var button = Element("Button", "Start", "startButton", canInvoke: true);
        var root = Element("Window", "Fixture", "root", children: [button]);
        var context = CreateContext();
        var attachment = CreateAttachment();
        var action = CreateAction(root, button, UiCommandKind.InvokeNodeAction);
        var tree = new UiTree(
            new UiSnapshotMetadata("alpha", "tests", DateTimeOffset.UtcNow, 1, 1, "Native Fixture", new Dictionary<string, string?>()),
            new UiNode(new UiNodeId("root"), "Window", "Fixture", "Fixture", null, true, true, false, [], [action.Node]));
        var session = CreateSessionSnapshot();
        var uiState = SessionUiState.Create(session.SessionId) with { ProjectedTree = tree };
        var refresh = new StubSessionUiRefreshService(uiState);
        var options = CreateOptions();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var executor = new UiCommandExecutor(
            new StubSessionCoordinator(session, uiState),
            new StubDesktopTargetProfileResolver(context),
            new StubSessionAttachmentOperations(attachment),
            new InMemoryExecutionCoordinator(options, clock, NullLogger<InMemoryExecutionCoordinator>.Instance),
            new MultiSessionHost.Desktop.Targets.DefaultExecutionResourceResolver(options, clock),
            refresh,
            new DefaultUiActionResolver(),
            [CreateAdapter(root)],
            new RecordingObservabilityRecorder(),
            clock,
            NullLogger<UiCommandExecutor>.Instance);

        var result = await executor.ExecuteAsync(UiCommand.InvokeNodeAction(session.SessionId, action.Node.Id), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.UpdatedUiStateAvailable);
        Assert.Equal(1, button.InvokeCalls);
        Assert.Equal(1, refresh.RefreshCalls);
    }

    private static NativeUiAutomationElementLocator CreateLocator(FakeNativeUiAutomationElement root) =>
        new(new StubElementProvider(root), new NativeUiAutomationIdentityBuilder());

    private static WindowsUiAutomationUiInteractionAdapter CreateAdapter(
        FakeNativeUiAutomationElement root,
        ISessionRecoveryStateStore? recoveryStore = null,
        RecordingObservabilityRecorder? recorder = null,
        bool windowAvailable = true)
    {
        var options = CreateOptions();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var process = new DesktopProcessInfo(100, "NativeApp", null, 200);
        var window = new DesktopWindowInfo(200, 100, "Native Fixture", windowAvailable);
        return new WindowsUiAutomationUiInteractionAdapter(
            CreateLocator(root),
            new DisabledNativeInputFallbackExecutor(),
            new InMemoryAttachedSessionStore(),
            new StubProcessLocator(process),
            new StubWindowLocator(windowAvailable ? window : null),
            recoveryStore ?? new InMemorySessionRecoveryStateStore(options, clock),
            recorder ?? new RecordingObservabilityRecorder(),
            options,
            clock);
    }

    private static SessionHostOptions CreateOptions() =>
        new()
        {
            Sessions = [TestOptionsFactory.Session("alpha")],
            NativeInteraction = new NativeInteractionOptions
            {
                ActionTimeoutMs = 500,
                RetryCount = 0,
                RetryDelayMs = 0,
                PostActionVerificationTimeoutMs = 100
            }
        };

    private static ResolvedUiAction CreateAction(
        FakeNativeUiAutomationElement root,
        FakeNativeUiAutomationElement target,
        UiCommandKind kind,
        string? actionName = null,
        string? textValue = null,
        bool? boolValue = null,
        string? selectedValue = null)
    {
        var node = FindAssignedNode(root, target);
        var uiNode = new UiNode(
            new UiNodeId(node.NodeId),
            node.Role,
            node.Name,
            node.Value ?? node.Name,
            node.Bounds,
            !node.IsOffscreen,
            node.IsEnabled,
            node.IsSelected ?? false,
            [
                new UiAttribute("automationId", node.AutomationId),
                new UiAttribute("runtimeId", node.RuntimeId),
                new UiAttribute("frameworkId", node.FrameworkId),
                new UiAttribute("className", node.ClassName),
                new UiAttribute("identityBasis", node.IdentityBasis),
                new UiAttribute("controlType", node.Role),
                new UiAttribute("acceptsText", node.Role == "TextBox" ? "true" : "false")
            ],
            []);
        var metadata = uiNode.Attributes.ToDictionary(static attribute => attribute.Name, static attribute => attribute.Value, StringComparer.Ordinal);
        return new ResolvedUiAction(kind, uiNode, actionName, textValue, boolValue, selectedValue, metadata);
    }

    private static NativeUiAutomationNode FindAssignedNode(FakeNativeUiAutomationElement root, FakeNativeUiAutomationElement target)
    {
        var assigned = new NativeUiAutomationIdentityBuilder().AssignIdentities(ToSnapshot(root));
        var path = FindPath(root, target) ?? throw new InvalidOperationException("Target is not under root.");
        var current = assigned;

        foreach (var index in path)
        {
            current = current.Children[index];
        }

        return current;
    }

    private static IReadOnlyList<int>? FindPath(FakeNativeUiAutomationElement current, FakeNativeUiAutomationElement target)
    {
        if (ReferenceEquals(current, target))
        {
            return [];
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            var childPath = FindPath(current.Children[index], target);
            if (childPath is not null)
            {
                return [index, .. childPath];
            }
        }

        return null;
    }

    private static NativeUiAutomationElementSnapshot ToSnapshot(FakeNativeUiAutomationElement element) =>
        new(
            element.Role,
            element.Name,
            element.AutomationId,
            element.RuntimeId,
            element.FrameworkId,
            element.ClassName,
            element.IsEnabled,
            element.IsOffscreen,
            element.HasKeyboardFocus,
            element.IsSelected,
            element.Value,
            element.Bounds,
            element.Metadata,
            element.Children.Select(ToSnapshot).ToArray());

    private static ResolvedDesktopTargetContext CreateContext()
    {
        var sessionId = new SessionId("alpha");
        var metadata = new Dictionary<string, string?> { ["NativeUiAutomation.MaxDepth"] = "6" };
        var target = new DesktopSessionTarget(sessionId, "native-profile", DesktopTargetKind.WindowsUiAutomationDesktop, DesktopSessionMatchingMode.WindowTitle, "NativeApp", "Native Fixture", null, null, metadata);
        var profile = new DesktopTargetProfile("native-profile", DesktopTargetKind.WindowsUiAutomationDesktop, "NativeApp", "Native Fixture", null, null, DesktopSessionMatchingMode.WindowTitle, metadata, SupportsUiSnapshots: true, SupportsStateEndpoint: false);
        return new ResolvedDesktopTargetContext(sessionId, profile, new SessionTargetBinding(sessionId, "native-profile", new Dictionary<string, string>(), null), target, new Dictionary<string, string>());
    }

    private static DesktopSessionAttachment CreateAttachment()
    {
        var context = CreateContext();
        return new DesktopSessionAttachment(
            context.SessionId,
            context.Target,
            new DesktopProcessInfo(100, "NativeApp", null, 200),
            new DesktopWindowInfo(200, 100, "Native Fixture", true),
            null,
            DateTimeOffset.UtcNow);
    }

    private static SessionSnapshot CreateSessionSnapshot()
    {
        var definition = CreateOptions().ToSessionDefinitions().Single();
        return new SessionSnapshot(
            definition,
            SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with { DesiredStatus = SessionStatus.Running, CurrentStatus = SessionStatus.Running },
            PendingWorkItems: 0);
    }

    private static FakeNativeUiAutomationElement Element(
        string role,
        string? name,
        string? automationId,
        string? value = null,
        bool? isSelected = null,
        bool canInvoke = false,
        bool canToggle = false,
        bool canSetValue = false,
        bool canSelect = false,
        IReadOnlyList<FakeNativeUiAutomationElement>? children = null) =>
        new(role, name, automationId, value, isSelected, canInvoke, canToggle, canSetValue, canSelect, children ?? []);

    private sealed class FakeNativeUiAutomationElement : INativeUiAutomationElement
    {
        public FakeNativeUiAutomationElement(
            string role,
            string? name,
            string? automationId,
            string? value,
            bool? isSelected,
            bool canInvoke,
            bool canToggle,
            bool canSetValue,
            bool canSelect,
            IReadOnlyList<FakeNativeUiAutomationElement> children)
        {
            Role = role;
            Name = name;
            AutomationId = automationId;
            Value = value;
            IsSelected = isSelected;
            CanInvoke = canInvoke;
            CanToggle = canToggle;
            CanSetValue = canSetValue;
            CanSelect = canSelect;
            Children = children;
        }

        public string Role { get; }

        public string? Name { get; }

        public string? AutomationId { get; }

        public string? RuntimeId => null;

        public string? FrameworkId => "Win32";

        public string? ClassName => $"{Role}Class";

        public bool IsEnabled => true;

        public bool IsOffscreen => false;

        public bool HasKeyboardFocus { get; private set; }

        public bool? IsSelected { get; private set; }

        public string? Value { get; private set; }

        public UiBounds? Bounds => new(10, 10, 100, 24);

        public IReadOnlyDictionary<string, string?> Metadata => new Dictionary<string, string?>(StringComparer.Ordinal);

        public IReadOnlyList<FakeNativeUiAutomationElement> Children { get; }

        public bool CanInvoke { get; }

        public bool CanToggle { get; }

        public bool CanSetValue { get; }

        public bool CanSelect { get; }

        public int InvokeCalls { get; private set; }

        public int ToggleCalls { get; private set; }

        public int SelectCalls { get; private set; }

        public IReadOnlyList<INativeUiAutomationElement> GetChildren(NativeUiAutomationCaptureOptions options) => Children;

        public bool TrySetFocus()
        {
            HasKeyboardFocus = true;
            return true;
        }

        public bool TryInvoke()
        {
            if (!CanInvoke)
            {
                return false;
            }

            InvokeCalls++;
            return true;
        }

        public bool TrySelect()
        {
            if (!CanSelect)
            {
                return false;
            }

            SelectCalls++;
            IsSelected = true;
            return true;
        }

        public bool TryExpand() => false;

        public bool TryCollapse() => false;

        public bool TryToggle()
        {
            if (!CanToggle)
            {
                return false;
            }

            ToggleCalls++;
            IsSelected = !(IsSelected ?? false);
            Value = IsSelected == true ? "On" : "Off";
            return true;
        }

        public bool TrySetValue(string value)
        {
            if (!CanSetValue)
            {
                return false;
            }

            Value = value;
            return true;
        }

        public bool TryLegacyDefaultAction() => false;
    }

    private sealed class StubElementProvider : INativeUiAutomationElementProvider
    {
        private readonly INativeUiAutomationElement _root;

        public StubElementProvider(INativeUiAutomationElement root)
        {
            _root = root;
        }

        public INativeUiAutomationElement GetRoot(DesktopSessionAttachment attachment) => _root;
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly DesktopProcessInfo? _process;

        public StubProcessLocator(DesktopProcessInfo? process)
        {
            _process = process;
        }

        public IReadOnlyCollection<DesktopProcessInfo> GetProcesses(string? processName = null) =>
            _process is null ? [] : [_process];

        public DesktopProcessInfo? GetProcessById(int processId) =>
            _process is not null && _process.ProcessId == processId ? _process : null;
    }

    private sealed class StubWindowLocator : IWindowLocator
    {
        private readonly DesktopWindowInfo? _window;

        public StubWindowLocator(DesktopWindowInfo? window)
        {
            _window = window;
        }

        public IReadOnlyCollection<DesktopWindowInfo> GetWindows() =>
            _window is null ? [] : [_window];

        public DesktopWindowInfo? GetWindowByHandle(long handle) =>
            _window is not null && _window.WindowHandle == handle ? _window : null;
    }

    private sealed class StubSessionCoordinator : ISessionCoordinator
    {
        private readonly SessionSnapshot _session;
        private readonly SessionUiState _uiState;

        public StubSessionCoordinator(SessionSnapshot session, SessionUiState uiState)
        {
            _session = session;
            _uiState = uiState;
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunSchedulerCycleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeSessionAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public IReadOnlyCollection<SessionSnapshot> GetSessions() => [_session];

        public SessionSnapshot? GetSession(SessionId sessionId) => sessionId == _session.SessionId ? _session : null;

        public SessionUiState? GetSessionUiState(SessionId sessionId) => sessionId == _session.SessionId ? _uiState : null;

        public SessionDomainState? GetSessionDomainState(SessionId sessionId) => null;

        public IReadOnlyCollection<SessionDomainState> GetSessionDomainStates() => [];

        public Task<SessionUiState> RefreshSessionUiAsync(SessionId sessionId, CancellationToken cancellationToken) => Task.FromResult(_uiState);

        public ProcessHealthSnapshot GetProcessHealth() => new(DateTimeOffset.UtcNow, 1, 0, 0, 0, 0, 0, []);

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class StubSessionAttachmentOperations : ISessionAttachmentOperations
    {
        private readonly DesktopSessionAttachment _attachment;

        public StubSessionAttachmentOperations(DesktopSessionAttachment attachment)
        {
            _attachment = attachment;
        }

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, CancellationToken cancellationToken) =>
            Task.FromResult(_attachment);

        public Task<bool> InvalidateAsync(SessionId sessionId, DesktopSessionAttachment currentAttachment, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubSessionUiRefreshService : ISessionUiRefreshService
    {
        private readonly SessionUiState _uiState;

        public StubSessionUiRefreshService(SessionUiState uiState)
        {
            _uiState = uiState;
        }

        public int RefreshCalls { get; private set; }

        public Task<SessionUiState> CaptureAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken) =>
            Task.FromResult(_uiState);

        public Task<SessionUiState> ProjectAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment? attachment, CancellationToken cancellationToken) =>
            Task.FromResult(_uiState);

        public Task<SessionUiState> RefreshAsync(SessionSnapshot snapshot, ResolvedDesktopTargetContext context, DesktopSessionAttachment attachment, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(_uiState);
        }
    }

    private sealed class RecordingObservabilityRecorder : NoOpObservabilityRecorder
    {
        public List<string> Stages { get; } = [];

        public List<string> AdapterErrors { get; } = [];

        public override ValueTask RecordActivityAsync(SessionId sessionId, string stage, string outcome, TimeSpan duration, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
        {
            Stages.Add(stage);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RecordAdapterErrorAsync(SessionId sessionId, string adapterName, string operation, Exception exception, string? reasonCode, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken)
        {
            AdapterErrors.Add(operation);
            return ValueTask.CompletedTask;
        }
    }
}
