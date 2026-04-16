using System.Globalization;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class EveLikeSemanticPackage : ITargetSemanticPackage
{
    public const string SemanticPackageName = "EveLike";
    public const string SemanticPackageVersion = "6.3.0";

    private readonly IUiTreeQueryService _query;

    public EveLikeSemanticPackage(IUiTreeQueryService query)
    {
        _query = query;
    }

    public string PackageName => SemanticPackageName;

    public string PackageVersion => SemanticPackageVersion;

    public ValueTask<TargetSemanticPackageResult> ExtractAsync(TargetSemanticPackageContext context, CancellationToken cancellationToken)
    {
        var visibleNodes = _query.Flatten(context.SemanticContext.UiTree)
            .Where(static node => node.Visible)
            .ToArray();

        var presence = ExtractPresence(context, visibleNodes, out var presenceWarnings);
        var route = ExtractRoute(context, visibleNodes, out var routeWarnings);
        var overview = ExtractOverview(context, visibleNodes, out var overviewWarnings);
        var probes = ExtractProbeScanner(context, visibleNodes, out var probeWarnings);
        var tactical = BuildTactical(context, overview, probes, out var tacticalWarnings);
        var safety = BuildSafety(context, visibleNodes, route, overview, out var safetyWarnings);

        var warnings = new List<string>();
        warnings.AddRange(presenceWarnings);
        warnings.AddRange(routeWarnings);
        warnings.AddRange(overviewWarnings);
        warnings.AddRange(probeWarnings);
        warnings.AddRange(tacticalWarnings);
        warnings.AddRange(safetyWarnings);

        var confidenceSummary = new Dictionary<string, DetectionConfidence>(StringComparer.Ordinal)
        {
            ["presence"] = presence.Confidence,
            ["route"] = route.Confidence,
            ["overview"] = overview.Count == 0 ? DetectionConfidence.Unknown : overview.Max(static item => item.Confidence),
            ["probeScanner"] = probes.Count == 0 ? DetectionConfidence.Unknown : probes.Max(static item => item.Confidence),
            ["tactical"] = tactical.Confidence,
            ["safety"] = safety.Confidence
        };

        var package = new EveLikeSemanticPackageResult(
            PackageName,
            PackageVersion,
            presence,
            route,
            overview,
            probes,
            tactical,
            safety,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            confidenceSummary);

        return ValueTask.FromResult(new TargetSemanticPackageResult(
            PackageName,
            PackageVersion,
            true,
            SummarizeConfidence(confidenceSummary.Values),
            package.Warnings,
            confidenceSummary,
            FailureReason: null,
            EveLike: package));
    }

    private LocalPresenceSnapshot ExtractPresence(TargetSemanticPackageContext context, IReadOnlyList<UiNode> visibleNodes, out List<string> warnings)
    {
        warnings = [];

        var panel = FindBestNode(visibleNodes, "local", "presence", "member", "roster");
        var entityNodes = panel is null
            ? []
            : CollectListItemNodes(panel).ToArray();

        var entities = new List<PresenceEntitySemantic>();
        foreach (var node in entityNodes)
        {
            var label = SemanticParsing.LabelFor(node, _query);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var tags = BuildTags(node, label);
            var standing = FirstText(node, "standing", "status", "disposition") ?? InferStanding(label, tags);
            var count = SemanticParsing.ParseInt(_query.GetAttribute(node, "count"));

            entities.Add(new PresenceEntitySemantic(
                label,
                standing,
                tags,
                [node.Id.Value],
                count,
                standing is null ? DetectionConfidence.Medium : DetectionConfidence.High));
        }

        if (panel is null && context.GenericResult.PresenceEntities.Count > 0)
        {
            warnings.Add("Presence panel was not explicitly identified; generic presence signals were reused.");
        }

        if (panel is not null && entities.Count == 0)
        {
            warnings.Add($"Presence panel '{panel.Id.Value}' was identified but no entities were extracted.");
        }

        var totalCount = SemanticParsing.ParseInt(_query.GetAttribute(panel ?? visibleNodes.FirstOrDefault(), "itemCount"));

        return new LocalPresenceSnapshot(
            panel is not null || entities.Count > 0,
            panel is null ? null : SemanticParsing.LabelFor(panel, _query),
            entities.Count,
            totalCount,
            entities.Take(50).ToArray(),
            entities.Count == 0 ? DetectionConfidence.Unknown : SummarizeConfidence(entities.Select(static item => item.Confidence)),
            warnings);
    }

    private TravelRouteSnapshot ExtractRoute(TargetSemanticPackageContext context, IReadOnlyList<UiNode> visibleNodes, out List<string> warnings)
    {
        warnings = [];

        var routePanel = FindBestNode(visibleNodes, "route", "waypoint", "destination", "autopilot", "travel");
        var waypointNodes = routePanel is null
            ? []
            : CollectListItemNodes(routePanel).ToArray();

        var waypoints = waypointNodes
            .Select(node => SemanticParsing.LabelFor(node, _query))
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Select(static label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var destination = routePanel is null ? null : FirstText(routePanel, "destination", "currentDestination", "routeDestination");
        var currentLocation = FirstText(routePanel, "currentLocation", "currentSystem", "location", "system")
            ?? SemanticParsing.LabelFor(routePanel ?? visibleNodes.FirstOrDefault(), _query);
        var nextWaypoint = FirstText(routePanel, "nextWaypoint", "next", "waypoint");
        var active = routePanel is not null || ContainsAny(destination, currentLocation, nextWaypoint, "route", "travel", "autopilot", "waypoint");
        var progress = SemanticParsing.GetPercent(routePanel ?? visibleNodes.FirstOrDefault(), _query);
        var reasons = new List<string>();

        if (routePanel is not null)
        {
            reasons.Add($"Route panel '{routePanel.Id.Value}' was identified.");
        }

        if (waypoints.Length == 0)
        {
            warnings.Add("Route panel was identified but no waypoint labels were extracted.");
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            reasons.Add($"Destination '{destination}' was visible.");
        }

        return new TravelRouteSnapshot(
            active,
            destination,
            currentLocation,
            nextWaypoint,
            waypoints.Length,
            waypoints.Take(25).ToArray(),
            progress,
            active ? DetectionConfidence.Medium : DetectionConfidence.Unknown,
            reasons.Concat(warnings).Distinct(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<OverviewEntrySemantic> ExtractOverview(TargetSemanticPackageContext context, IReadOnlyList<UiNode> visibleNodes, out List<string> warnings)
    {
        warnings = [];

        var panel = FindBestNode(visibleNodes, "overview", "overview list", "tactical overview");
        var rows = panel is null
            ? []
            : CollectListItemNodes(panel).ToArray();

        var entries = new List<OverviewEntrySemantic>();

        foreach (var node in rows)
        {
            var label = SemanticParsing.LabelFor(node, _query);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var distanceText = FirstText(node, "distance", "range", "distanceText");
            var distanceValue = ParseDistance(distanceText);
            var category = FirstText(node, "category", "type", "group", "objectType");
            var disposition = InferDisposition(label, category, node);
            var selected = node.Selected || SemanticParsing.IsTrue(_query.GetAttribute(node, "selected"));
            var targeted = SemanticParsing.IsTrue(_query.GetAttribute(node, "targeted")) || SemanticParsing.ContainsAny(label, "target", "locked", "active");

            entries.Add(new OverviewEntrySemantic(
                label,
                category,
                distanceText,
                distanceValue,
                selected,
                targeted,
                disposition,
                [node.Id.Value],
                targeted || selected ? DetectionConfidence.High : DetectionConfidence.Medium,
                []));
        }

        if (panel is null && context.GenericResult.Lists.Count > 0)
        {
            warnings.Add("Overview panel was not explicitly identified; generic list-like signals were reused.");
        }

        return entries.Take(50).ToArray();
    }

    private IReadOnlyList<ProbeScannerEntrySemantic> ExtractProbeScanner(TargetSemanticPackageContext context, IReadOnlyList<UiNode> visibleNodes, out List<string> warnings)
    {
        warnings = [];

        var panel = FindBestNode(visibleNodes, "probe scanner", "scanner", "anomaly", "signature");
        var rows = panel is null
            ? []
            : CollectListItemNodes(panel).ToArray();

        var entries = new List<ProbeScannerEntrySemantic>();

        foreach (var node in rows)
        {
            var label = SemanticParsing.LabelFor(node, _query);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var distanceText = FirstText(node, "distance", "range", "distanceText");
            var status = FirstText(node, "status", "scanStatus", "result");
            var signatureType = FirstText(node, "signatureType", "siteType", "anomalyType") ?? InferProbeType(label, status);

            entries.Add(new ProbeScannerEntrySemantic(
                label,
                signatureType,
                status,
                distanceText,
                ParseDistance(distanceText),
                [node.Id.Value],
                DetectionConfidence.Medium,
                []));
        }

        if (panel is null && context.GenericResult.Alerts.Count > 0)
        {
            warnings.Add("Probe scanner panel was not explicitly identified; alert signals were reused as hints.");
        }

        return entries.Take(50).ToArray();
    }

    private TacticalSnapshot BuildTactical(
        TargetSemanticPackageContext context,
        IReadOnlyList<OverviewEntrySemantic> overview,
        IReadOnlyList<ProbeScannerEntrySemantic> probes,
        out List<string> warnings)
    {
        warnings = [];

        var hostileCandidates = overview.Count(entry => string.Equals(entry.Disposition, "Hostile", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Disposition, "Threat", StringComparison.OrdinalIgnoreCase) ||
            entry.Warnings.Any(warning => warning.Contains("hostile", StringComparison.OrdinalIgnoreCase)));

        var selectedTargets = context.GenericResult.Targets
            .Where(static target => target.Selected || target.Active)
            .Select(static target => target.Label)
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Select(static label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var alerts = context.GenericResult.Alerts
            .Where(static alert => alert.Visible)
            .OrderByDescending(static alert => alert.Severity)
            .Select(static alert => alert.Message)
            .Where(static alert => !string.IsNullOrWhiteSpace(alert))
            .Select(static alert => alert!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var primaryObjects = overview
            .Where(static entry => entry.Selected || entry.Targeted || entry.Disposition is not null)
            .Take(10)
            .ToArray();

        if (hostileCandidates > 0)
        {
            warnings.Add($"{hostileCandidates} overview entries appeared hostile or threat-like.");
        }

        return new TacticalSnapshot(
            primaryObjects,
            hostileCandidates,
            selectedTargets,
            probes.Select(static entry => entry.Label).Where(static label => !string.IsNullOrWhiteSpace(label)).Select(static label => label!).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            alerts,
            hostileCandidates > 0 || selectedTargets.Length > 0 || alerts.Length > 0 ? DetectionConfidence.Medium : DetectionConfidence.Unknown,
            warnings);
    }

    private SafetyLocationSemantic BuildSafety(
        TargetSemanticPackageContext context,
        IReadOnlyList<UiNode> visibleNodes,
        TravelRouteSnapshot route,
        IReadOnlyList<OverviewEntrySemantic> overview,
        out List<string> warnings)
    {
        warnings = [];

        var safeNode = FindBestNode(visibleNodes, "safe", "hide", "dock", "station", "tether", "escape");
        var safeLabel = safeNode is null ? null : SemanticParsing.LabelFor(safeNode, _query);
        var docked = ContainsAny(safeLabel, "dock", "station", "hangar") || ContainsAny(route.CurrentLocationLabel, "station", "dock", "hangar");
        var tethered = ContainsAny(safeLabel, "tether") || overview.Any(entry => entry.Warnings.Any(warning => warning.Contains("tether", StringComparison.OrdinalIgnoreCase)));
        var hideAvailable = ContainsAny(safeLabel, "hide", "safe", "cloak", "safe spot") || overview.Any(entry => ContainsAny(entry.Label, "safe", "hide"));
        var escapeRoute = route.NextWaypointLabel ?? route.DestinationLabel;
        var isSafe = docked || tethered || hideAvailable;
        var reasons = new List<string>();

        if (safeNode is not null)
        {
            reasons.Add($"Safety marker '{safeNode.Id.Value}' was visible.");
        }

        if (docked)
        {
            reasons.Add("Docked or station-like cues were visible.");
        }

        if (hideAvailable)
        {
            reasons.Add("Hide or safe-location cues were visible.");
        }

        if (!isSafe)
        {
            warnings.Add("No strong safe-location cues were identified.");
        }

        return new SafetyLocationSemantic(
            isSafe,
            safeLabel,
            hideAvailable,
            docked,
            tethered,
            escapeRoute,
            isSafe ? DetectionConfidence.Medium : DetectionConfidence.Low,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private IReadOnlyList<UiNode> CollectListItemNodes(UiNode node) =>
        _query.EnumerateVisibleDescendants(node)
            .Where(static child => child.Role.Contains("item", StringComparison.OrdinalIgnoreCase) ||
                child.Role.Contains("row", StringComparison.OrdinalIgnoreCase) ||
                child.Role.Contains("entry", StringComparison.OrdinalIgnoreCase) ||
                child.Children.Count == 0)
            .ToArray();

    private UiNode? FindBestNode(IReadOnlyList<UiNode> nodes, params string[] fragments) =>
        nodes.FirstOrDefault(node => MatchesAny(node, fragments));

    private bool MatchesAny(UiNode node, params string[] fragments)
    {
        var candidates = _query.GatherTextCandidates(node);
        return fragments.Any(fragment => candidates.Any(candidate => candidate.Contains(fragment, StringComparison.OrdinalIgnoreCase))) ||
            fragments.Any(fragment => node.Role.Contains(fragment, StringComparison.OrdinalIgnoreCase)) ||
            fragments.Any(fragment => _query.GetAttribute(node, "semanticRole")?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
    }

    private string? FirstText(UiNode node, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            var value = _query.GetAttribute(node, attributeName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool ContainsAny(params string?[] values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) && SemanticParsing.ContainsAny(value, "route", "travel", "autopilot", "waypoint", "safe", "hide", "dock", "station", "tether", "hostile", "threat"));

    private static string? InferStanding(string? label, IReadOnlyList<string> tags)
    {
        var text = string.Join(' ', new[] { label }.Concat(tags));

        if (SemanticParsing.ContainsAny(text, "hostile", "threat", "enemy", "red"))
        {
            return "Hostile";
        }

        if (SemanticParsing.ContainsAny(text, "friendly", "blue", "ally", "corp", "alliance"))
        {
            return "Friendly";
        }

        if (SemanticParsing.ContainsAny(text, "neutral", "white"))
        {
            return "Neutral";
        }

        return SemanticParsing.ContainsAny(text, "unknown", "?", "unidentified") ? "Unknown" : null;
    }

    private static IReadOnlyList<string> BuildTags(UiNode node, string label)
    {
        var tags = new List<string>();

        if (node.Selected)
        {
            tags.Add("selected");
        }

        if (SemanticParsing.IsTrue(node.Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, "targeted", StringComparison.OrdinalIgnoreCase))?.Value))
        {
            tags.Add("targeted");
        }

        tags.AddRange(label.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4));
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? InferDisposition(string? label, string? category, UiNode node)
    {
        var text = string.Join(' ', new[] { label, category, node.Role });

        if (SemanticParsing.ContainsAny(text, "hostile", "threat", "enemy"))
        {
            return "Hostile";
        }

        if (SemanticParsing.ContainsAny(text, "friendly", "ally", "blue", "corp", "fleet"))
        {
            return "Friendly";
        }

        if (SemanticParsing.ContainsAny(text, "neutral", "unknown"))
        {
            return "Neutral";
        }

        return null;
    }

    private static string? InferProbeType(string? label, string? status)
    {
        var text = string.Join(' ', new[] { label, status });

        if (SemanticParsing.ContainsAny(text, "anomaly"))
        {
            return "Anomaly";
        }

        if (SemanticParsing.ContainsAny(text, "signature"))
        {
            return "Signature";
        }

        if (SemanticParsing.ContainsAny(text, "site"))
        {
            return "Site";
        }

        return null;
    }

    private static double? ParseDistance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(static ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray()).Replace(',', '.');
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DetectionConfidence SummarizeConfidence(IEnumerable<DetectionConfidence> values)
    {
        var materialized = values.ToArray();
        return materialized.Length == 0 ? DetectionConfidence.Unknown : materialized.Max();
    }
}