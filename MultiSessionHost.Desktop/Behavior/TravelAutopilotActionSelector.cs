using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.UiModel.Extensions;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class TravelAutopilotActionSelector : ITravelAutopilotActionSelector
{
    private readonly IUiTreeQueryService _query;
    private readonly ScreenTravelActionSelector? _screenSelector;

    public TravelAutopilotActionSelector(IUiTreeQueryService query)
        : this(query, null, null, null, null)
    {
    }

    public TravelAutopilotActionSelector(
        IUiTreeQueryService query,
        ISessionScreenRegionStore? screenRegionStore,
        ISessionFramePreprocessingStore? preprocessingStore,
        ISessionOcrExtractionStore? ocrStore,
        ISessionTemplateDetectionStore? templateStore)
    {
        _query = query;
        _screenSelector = screenRegionStore is null || preprocessingStore is null || ocrStore is null || templateStore is null
            ? null
            : new ScreenTravelActionSelector(screenRegionStore, preprocessingStore, ocrStore, templateStore);
    }

    public async ValueTask<TravelAutopilotActionSelection?> SelectActionAsync(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        CancellationToken cancellationToken)
    {
        if (context.TargetContext.Target.Kind == DesktopTargetKind.ScreenCaptureDesktop && intent != TravelAutopilotActionIntent.RefreshUi && _screenSelector is not null)
        {
            var screenSelection = await _screenSelector.SelectActionAsync(context, package, memoryState, intent, cancellationToken).ConfigureAwait(false);
            if (screenSelection is not null)
            {
                return screenSelection;
            }
        }

        var tree = context.SessionUiState?.ProjectedTree;
        if (tree is null)
        {
            return intent == TravelAutopilotActionIntent.RefreshUi
                ? CreateRefreshSelection(context, package, memoryState)
                : null;
        }

        return intent switch
        {
            TravelAutopilotActionIntent.RefreshUi => CreateRefreshSelection(context, package, memoryState),
            TravelAutopilotActionIntent.ToggleAutopilot => SelectToggleAutopilot(context, package, tree, memoryState),
            TravelAutopilotActionIntent.SelectWaypoint => SelectWaypoint(context, package, tree, memoryState),
            TravelAutopilotActionIntent.InvokeTravelControl => SelectTravelControl(context, package, tree, memoryState),
            _ => null
        };
    }

    private TravelAutopilotActionSelection CreateRefreshSelection(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState)
    {
        var metadata = BuildMetadata(context, package, memoryState, TravelAutopilotActionIntent.RefreshUi, null, null);
        metadata["uiCommandKind"] = UiCommandKind.RefreshUi.ToString();
        return new TravelAutopilotActionSelection(
            TravelAutopilotActionIntent.RefreshUi,
            UiCommand.RefreshUi(context.SessionSnapshot.SessionId, metadata: ToNullableMetadata(metadata)),
            BehaviorReasonCodes.RefreshRequired,
            BehaviorReasonCodes.RefreshRequired,
            "Travel planning needs a fresh UI snapshot.",
            metadata,
            null);
    }

    private TravelAutopilotActionSelection? SelectToggleAutopilot(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        UiTree tree,
        TravelAutopilotMemoryState memoryState)
    {
        var candidate = FindNode(tree, "autopilot", "auto pilot", "travel mode", "navigation");
        if (candidate is null)
        {
            return null;
        }

        var kind = candidate.Role is "CheckBox" or "ToggleButton" or "Switch"
            ? UiCommandKind.ToggleNode
            : SupportsAction(candidate, "toggle") ? UiCommandKind.ToggleNode : SupportsAction(candidate, "invoke") ? UiCommandKind.InvokeNodeAction : UiCommandKind.ClickNode;

        var metadata = BuildMetadata(context, package, memoryState, TravelAutopilotActionIntent.ToggleAutopilot, candidate.Id.Value, candidate.Text ?? candidate.Name);
        metadata["uiCommandKind"] = kind.ToString();
        if (kind == UiCommandKind.InvokeNodeAction)
        {
            metadata["uiActionName"] = "toggle";
        }
        else if (kind == UiCommandKind.ToggleNode)
        {
            metadata["uiBoolValue"] = bool.TrueString;
        }
        var command = BuildCommand(context.SessionSnapshot.SessionId, kind, candidate.Id, actionName: kind == UiCommandKind.InvokeNodeAction ? "toggle" : null, selectedValue: null, metadata);

        return new TravelAutopilotActionSelection(
            TravelAutopilotActionIntent.ToggleAutopilot,
            command,
            BehaviorReasonCodes.PlanToggleAutopilot,
            BehaviorReasonCodes.PlanToggleAutopilot,
            $"Selected autopilot control '{candidate.Id.Value}'.",
            metadata,
            null);
    }

    private TravelAutopilotActionSelection? SelectWaypoint(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        UiTree tree,
        TravelAutopilotMemoryState memoryState)
    {
        var routePanel = FindNode(tree, "route", "waypoint", "destination", "travel");
        var nextWaypoint = package.TravelRoute.NextWaypointLabel ?? package.TravelRoute.VisibleWaypoints.FirstOrDefault();
        if (routePanel is null || string.IsNullOrWhiteSpace(nextWaypoint))
        {
            return null;
        }

        var metadata = BuildMetadata(context, package, memoryState, TravelAutopilotActionIntent.SelectWaypoint, routePanel.Id.Value, nextWaypoint);
        metadata["uiCommandKind"] = UiCommandKind.SelectItem.ToString();
        metadata["uiSelectedValue"] = nextWaypoint;
        var command = BuildCommand(
            context.SessionSnapshot.SessionId,
            UiCommandKind.SelectItem,
            routePanel.Id,
            actionName: null,
            selectedValue: nextWaypoint,
            metadata);

        return new TravelAutopilotActionSelection(
            TravelAutopilotActionIntent.SelectWaypoint,
            command,
            BehaviorReasonCodes.PlanSelectWaypoint,
            BehaviorReasonCodes.PlanSelectWaypoint,
            $"Selected next waypoint '{nextWaypoint}' from route panel '{routePanel.Id.Value}'.",
            metadata,
            null);
    }

    private TravelAutopilotActionSelection? SelectTravelControl(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        UiTree tree,
        TravelAutopilotMemoryState memoryState)
    {
        var candidate = FindNode(tree, "warp", "jump", "dock", "continue", "resume", "travel", "navigate");
        if (candidate is null)
        {
            return null;
        }

        var kind = SupportsAction(candidate, "invoke") ? UiCommandKind.InvokeNodeAction : SupportsAction(candidate, "toggle") ? UiCommandKind.ToggleNode : UiCommandKind.ClickNode;
        var metadata = BuildMetadata(context, package, memoryState, TravelAutopilotActionIntent.InvokeTravelControl, candidate.Id.Value, candidate.Text ?? candidate.Name);
        metadata["uiCommandKind"] = kind.ToString();
        if (kind == UiCommandKind.InvokeNodeAction)
        {
            metadata["uiActionName"] = "default";
        }
        else if (kind == UiCommandKind.ToggleNode)
        {
            metadata["uiBoolValue"] = bool.TrueString;
        }
        var command = BuildCommand(context.SessionSnapshot.SessionId, kind, candidate.Id, actionName: kind == UiCommandKind.InvokeNodeAction ? "default" : null, selectedValue: null, metadata);

        return new TravelAutopilotActionSelection(
            TravelAutopilotActionIntent.InvokeTravelControl,
            command,
            BehaviorReasonCodes.PlanProgressAction,
            BehaviorReasonCodes.PlanProgressAction,
            $"Selected travel control '{candidate.Id.Value}'.",
            metadata,
            null);
    }

    private static UiCommand BuildCommand(
        SessionId sessionId,
        UiCommandKind kind,
        UiNodeId? nodeId,
        string? actionName,
        string? selectedValue,
        IReadOnlyDictionary<string, string> metadata)
    {
        return kind switch
        {
            UiCommandKind.RefreshUi => UiCommand.RefreshUi(sessionId, metadata: ToNullableMetadata(metadata)),
            UiCommandKind.ClickNode => UiCommand.ClickNode(sessionId, nodeId ?? throw new InvalidOperationException("A node is required for click commands."), ToNullableMetadata(metadata)),
            UiCommandKind.InvokeNodeAction => UiCommand.InvokeNodeAction(sessionId, nodeId ?? throw new InvalidOperationException("A node is required for invoke commands."), actionName, ToNullableMetadata(metadata)),
            UiCommandKind.SelectItem => UiCommand.SelectItem(sessionId, nodeId ?? throw new InvalidOperationException("A node is required for select commands."), selectedValue, ToNullableMetadata(metadata)),
            UiCommandKind.ToggleNode => UiCommand.ToggleNode(sessionId, nodeId ?? throw new InvalidOperationException("A node is required for toggle commands."), boolValue: true, ToNullableMetadata(metadata)),
            _ => throw new InvalidOperationException($"UiCommandKind '{kind}' is not supported for travel autopilot planning.")
        };
    }

    private static IReadOnlyDictionary<string, string?> ToNullableMetadata(IReadOnlyDictionary<string, string> metadata) =>
        metadata.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> BuildMetadata(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        string? nodeId,
        string? label)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["behaviorPack"] = package.PackageName,
            ["behaviorPackVersion"] = package.PackageVersion,
            ["travelActionIntent"] = intent.ToString(),
            ["routeFingerprint"] = ComputeRouteFingerprint(package),
            ["routeActive"] = package.TravelRoute.RouteActive.ToString(),
            ["routeProgressPercent"] = package.TravelRoute.ProgressPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            ["routeDestinationLabel"] = package.TravelRoute.DestinationLabel ?? string.Empty,
            ["routeCurrentLocationLabel"] = package.TravelRoute.CurrentLocationLabel ?? string.Empty,
            ["routeNextWaypointLabel"] = package.TravelRoute.NextWaypointLabel ?? string.Empty,
            ["routeWaypointCount"] = package.TravelRoute.WaypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["routeVisibleWaypointCount"] = package.TravelRoute.VisibleWaypoints.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["stateVersion"] = context.SessionDomainState.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            metadata["uiNodeId"] = nodeId!;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            metadata["uiNodeLabel"] = label!;
        }

        if (!string.IsNullOrWhiteSpace(memoryState.LastActionCode))
        {
            metadata["lastActionCode"] = memoryState.LastActionCode!;
        }

        return metadata;
    }

    private static string ComputeRouteFingerprint(EveLikeSemanticPackageResult package)
    {
        var route = package.TravelRoute;
        return string.Join('|',
            package.PackageName,
            route.RouteActive,
            route.DestinationLabel ?? string.Empty,
            route.CurrentLocationLabel ?? string.Empty,
            route.NextWaypointLabel ?? string.Empty,
            route.WaypointCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            route.ProgressPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static bool SupportsAction(UiNode node, string actionName)
    {
        var actions = node.Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, "semanticActions", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(actions))
        {
            return false;
        }

        return actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => string.Equals(candidate, actionName, StringComparison.OrdinalIgnoreCase));
    }

    private UiNode? FindNode(UiTree tree, params string[] fragments)
    {
        foreach (var node in _query.Flatten(tree).Where(static node => node.Visible && node.Enabled))
        {
            var textCandidates = _query.GatherTextCandidates(node);
            var candidates = textCandidates
                .Concat([node.Name, node.Text, node.Role])
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .ToArray();

            if (fragments.Any(fragment => candidates.Any(candidate => candidate.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            {
                return node;
            }
        }

        return null;
    }
}
