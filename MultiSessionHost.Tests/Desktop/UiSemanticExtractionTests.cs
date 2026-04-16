using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class UiSemanticExtractionTests
{
    [Fact]
    public void QueryService_TraversesAndQueriesTree()
    {
        var tree = CreateSemanticTree();
        var query = new DefaultUiTreeQueryService();

        var flattened = query.Flatten(tree);
        var listNodes = query.FindByRole(tree, "ListBox");
        var alertNodes = query.FindByTextFragment(tree, "Alert");
        var progressNodes = query.FindByAttribute(tree, "semanticRole", "transit");

        Assert.Contains(flattened, node => node.Id.Value == "itemsList");
        Assert.Equal(2, listNodes.Count);
        Assert.Contains(alertNodes, node => node.Id.Value == "alertLabel");
        Assert.Single(progressNodes);
        Assert.Contains("Form:root", query.ComputeNodePath(tree, flattened.First(node => node.Id.Value == "alertLabel")));
    }

    [Fact]
    public void Classifier_ReturnsStableHeuristicClassifications()
    {
        var tree = CreateSemanticTree();
        var query = new DefaultUiTreeQueryService();
        var classifier = new DefaultUiSemanticClassifier();
        var nodes = query.Flatten(tree).ToDictionary(static node => node.Id.Value);

        Assert.Equal(ListKind.Items, classifier.ClassifyList(nodes["itemsList"], query).Kind);
        Assert.Equal(TargetKind.ActiveItem, classifier.ClassifyTarget(nodes["selectedLabel"], query).Kind);
        Assert.Equal(AlertSeverity.Warning, classifier.ClassifyAlert(nodes["alertLabel"], query).Kind);
        Assert.Equal(TransitStatus.InProgress, classifier.ClassifyTransit(nodes["progress"], query).Kind);
        Assert.Equal(ResourceKind.Health, classifier.ClassifyResource(nodes["health"], query).Kind);
        Assert.Equal(CapabilityStatus.Enabled, classifier.ClassifyCapability(nodes["capability"], query).Kind);
        Assert.Equal(PresenceEntityKind.Group, classifier.ClassifyPresenceEntity(nodes["presenceList"], query).Kind);
    }

    [Fact]
    public async Task Pipeline_DetectsAllFamiliesAndIsDeterministic()
    {
        var context = CreateContext(CreateSemanticTree());
        var pipeline = CreatePipeline();

        var first = await pipeline.ExtractAsync(context, CancellationToken.None);
        var second = await pipeline.ExtractAsync(context, CancellationToken.None);

        Assert.Single(first.Lists, static item => item.NodeId == "itemsList");
        Assert.Contains(first.Targets, static item => item.NodeId == "selectedLabel" && item.Active);
        Assert.Contains(first.Alerts, static item => item.NodeId == "alertLabel" && item.Severity == AlertSeverity.Warning);
        Assert.Contains(first.TransitStates, static item => item.NodeIds.Contains("progress") && item.ProgressPercent == 45);
        Assert.Contains(first.Resources, static item => item.NodeId == "health" && item.Percent == 30 && item.Degraded);
        Assert.Contains(first.Capabilities, static item => item.NodeId == "capability" && item.Enabled);
        Assert.Contains(first.PresenceEntities, static item => item.NodeId == "presenceList" && item.Count == 2);
        Assert.Equal(first.Lists.Select(static item => item.NodeId), second.Lists.Select(static item => item.NodeId));
        Assert.Equal(first.Targets.Select(static item => item.NodeId), second.Targets.Select(static item => item.NodeId));
        Assert.Equal(first.Alerts.Select(static item => item.NodeId), second.Alerts.Select(static item => item.NodeId));
    }

    [Fact]
    public async Task AmbiguousNodes_YieldWarningsInsteadOfInvalidHardClassifications()
    {
        var tree = new UiTree(
            new UiSnapshotMetadata("semantic-ambiguous", "UnitTest", DateTimeOffset.UtcNow, 1, 2, "Window", new Dictionary<string, string?>()),
            Node("root", "Panel", children:
            [
                Node("ambiguous", "Panel", children:
                [
                    Node("one", "Label", text: "Alpha"),
                    Node("two", "Label", text: "Beta"),
                    Node("three", "Label", text: "Gamma")
                ])
            ]));
        var result = await CreatePipeline().ExtractAsync(CreateContext(tree), CancellationToken.None);

        Assert.Contains(result.Warnings, warning => warning.Contains("looked list-like", StringComparison.Ordinal));
        Assert.Empty(result.Alerts);
    }

    private static UiSemanticExtractionPipeline CreatePipeline()
    {
        var query = new DefaultUiTreeQueryService();
        var classifier = new DefaultUiSemanticClassifier();

        return new UiSemanticExtractionPipeline(
        [
            new ListDetectorExtractor(query, classifier),
            new TargetDetectorExtractor(query, classifier),
            new AlertDetectorExtractor(query, classifier),
            new TransitStateDetectorExtractor(query, classifier),
            new ResourceCapabilityDetectorExtractor(query, classifier),
            new PresenceEntityDetectorExtractor(query, classifier)
        ], new DefaultTargetSemanticPackageResolver([]), new NoOpObservabilityRecorder());
    }

    private static UiSemanticExtractionContext CreateContext(UiTree tree)
    {
        var now = DateTimeOffset.Parse("2026-04-15T14:00:00Z");
        var sessionId = new SessionId("semantic-unit");
        var definition = new SessionDefinition(
            sessionId,
            "Semantic Unit",
            Enabled: true,
            TickInterval: TimeSpan.FromSeconds(1),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 3,
            InitialBackoff: TimeSpan.FromMilliseconds(50),
            Tags: []);
        var runtime = SessionRuntimeState.Create(definition, now) with
        {
            CurrentStatus = SessionStatus.Running,
            DesiredStatus = SessionStatus.Running
        };
        var snapshot = new SessionSnapshot(definition, runtime, PendingWorkItems: 0);
        var profile = new DesktopTargetProfile(
            "unit-test",
            DesktopTargetKind.DesktopTestApp,
            "UnitTest",
            WindowTitleFragment: null,
            CommandLineFragmentTemplate: null,
            BaseAddressTemplate: null,
            DesktopSessionMatchingMode.WindowTitle,
            new Dictionary<string, string?>(),
            SupportsUiSnapshots: true,
            SupportsStateEndpoint: true);
        var binding = new SessionTargetBinding(sessionId, profile.ProfileName, new Dictionary<string, string>(), Overrides: null);
        var target = new DesktopSessionTarget(
            sessionId,
            profile.ProfileName,
            profile.Kind,
            profile.MatchingMode,
            profile.ProcessName,
            WindowTitleFragment: null,
            CommandLineFragment: null,
            BaseAddress: null,
            new Dictionary<string, string?>());

        return new UiSemanticExtractionContext(
            sessionId,
            SessionUiState.Create(sessionId) with { ProjectedTree = tree },
            tree,
            SessionDomainState.CreateBootstrap(sessionId, now.AddMinutes(-1)),
            snapshot,
            new ResolvedDesktopTargetContext(sessionId, profile, binding, target, new Dictionary<string, string>()),
            Attachment: null,
            now);
    }

    private static UiTree CreateSemanticTree() =>
        new(
            new UiSnapshotMetadata("semantic-unit", "UnitTest", DateTimeOffset.UtcNow, 1, 2, "Window", new Dictionary<string, string?>()),
            Node(
                "root",
                "Form",
                text: "Semantic Root",
                children:
                [
                    Node("itemsList", "ListBox", name: "Items", selected: true, attributes:
                    [
                        new UiAttribute("itemCount", "3"),
                        new UiAttribute("selectedItemCount", "1"),
                        new UiAttribute("selectedItem", "Item 2"),
                        new UiAttribute("items", "[\"Item 1\",\"Item 2\",\"Item 3\"]"),
                        new UiAttribute("scrollable", "true")
                    ]),
                    Node("selectedLabel", "Label", text: "Selected: Item 2", attributes:
                    [
                        new UiAttribute("semanticRole", "target"),
                        new UiAttribute("active", "true")
                    ]),
                    Node("alertLabel", "Label", text: "Alert: warning", attributes:
                    [
                        new UiAttribute("semanticRole", "alert"),
                        new UiAttribute("severity", "Warning"),
                        new UiAttribute("source", "Alert")
                    ]),
                    Node("progress", "ProgressBar", name: "Progress", attributes:
                    [
                        new UiAttribute("semanticRole", "transit"),
                        new UiAttribute("value", "45"),
                        new UiAttribute("maximum", "100"),
                        new UiAttribute("valuePercent", "45")
                    ]),
                    Node("health", "ProgressBar", name: "Health resource", attributes:
                    [
                        new UiAttribute("semanticRole", "resource"),
                        new UiAttribute("value", "30"),
                        new UiAttribute("maximum", "100"),
                        new UiAttribute("valuePercent", "30")
                    ]),
                    Node("capability", "CheckBox", text: "Capabilities enabled", selected: true, attributes:
                    [
                        new UiAttribute("semanticRole", "capability"),
                        new UiAttribute("checked", "true"),
                        new UiAttribute("semanticActions", "click,toggle")
                    ]),
                    Node("presenceList", "ListBox", name: "Presence", attributes:
                    [
                        new UiAttribute("semanticRole", "presence"),
                        new UiAttribute("entityCount", "2"),
                        new UiAttribute("itemCount", "2"),
                        new UiAttribute("items", "[\"Presence 1\",\"Presence 2\"]")
                    ])
                ]));

    private static UiNode Node(
        string id,
        string role,
        string? name = null,
        string? text = null,
        bool visible = true,
        bool enabled = true,
        bool selected = false,
        IReadOnlyList<UiAttribute>? attributes = null,
        IReadOnlyList<UiNode>? children = null) =>
        new(
            new UiNodeId(id),
            role,
            name,
            text,
            Bounds: null,
            visible,
            enabled,
            selected,
            attributes ?? [],
            children ?? []);

    private sealed class NoOpObservabilityRecorder : IObservabilityRecorder
    {
        public ValueTask RecordActivityAsync(SessionId sessionId, string stage, string outcome, TimeSpan duration, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordPolicyEvaluationAsync(SessionId sessionId, string policyName, IReadOnlyList<PolicyEvaluationResult> policyResults, bool isPolicyPaused, TimeSpan duration, string outcome, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordDecisionPlanAsync(DecisionPlan plan, TimeSpan duration, string outcome, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordDecisionExecutionAsync(DecisionPlanExecutionResult executionResult, TimeSpan duration, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordCommandExecutionAsync(UiCommand command, UiCommandResult result, TimeSpan duration, string? adapterName, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordAttachmentAsync(SessionId sessionId, string operation, string adapterName, string outcome, TimeSpan duration, string? targetKind, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordPersistenceAsync(SessionId sessionId, string operation, string outcome, TimeSpan duration, string? path, int? itemCount, string? reasonCode, string? reason, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordAdapterErrorAsync(SessionId sessionId, string adapterName, string operation, Exception exception, string? reasonCode, string? sourceComponent, IReadOnlyDictionary<string, string>? metadata, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RecordDecisionReasonAsync(SessionId sessionId, string category, string reasonCode, string? reason, string? sourceComponent, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public SessionObservabilitySnapshot? GetSnapshot(SessionId sessionId) => null;
        public SessionObservabilityMetricsSnapshot? GetMetrics(SessionId sessionId) => null;
        public GlobalObservabilitySnapshot GetGlobalSnapshot() => new(DateTimeOffset.UtcNow, SessionObservabilityStatus.Idle, 0, 0, 0, 0, 0, [], []);
    }
}
