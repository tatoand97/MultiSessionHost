using System.Globalization;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class ScreenTravelActionSelector
{
    private readonly ISessionScreenRegionStore _screenRegionStore;
    private readonly ISessionFramePreprocessingStore _preprocessingStore;
    private readonly ISessionOcrExtractionStore _ocrStore;
    private readonly ISessionTemplateDetectionStore _templateStore;

    public ScreenTravelActionSelector(
        ISessionScreenRegionStore screenRegionStore,
        ISessionFramePreprocessingStore preprocessingStore,
        ISessionOcrExtractionStore ocrStore,
        ISessionTemplateDetectionStore templateStore)
    {
        _screenRegionStore = screenRegionStore;
        _preprocessingStore = preprocessingStore;
        _ocrStore = ocrStore;
        _templateStore = templateStore;
    }

    public async ValueTask<TravelAutopilotActionSelection?> SelectActionAsync(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent == TravelAutopilotActionIntent.RefreshUi)
        {
            return null;
        }

        var sessionId = context.SessionSnapshot.SessionId;
        var regions = await _screenRegionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var preprocessing = await _preprocessingStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var ocr = await _ocrStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var templates = await _templateStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (regions is null || preprocessing is null || ocr is null || templates is null)
        {
            return null;
        }

        var minConfidence = ParseDoubleMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelMinActionConfidence, 0.75d);
        var allowRegionFallback = ParseBoolMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelAllowRegionCenterFallback, false);
        var actionRegions = ParseCsvMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelActionRegions, ["window.right", "window.top", "window.center"]);
        var waypointTerms = ParseCsvMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelWaypointTerms, ["next waypoint", "waypoint", "route"]);
        var autopilotTerms = ParseCsvMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelAutopilotToggleTerms, ["autopilot", "auto pilot", "travel mode", "navigation"]);
        var controlTerms = ParseCsvMetadata(context.TargetContext.Target.Metadata, DesktopTargetMetadata.ScreenTravelControlTerms, ["warp", "jump", "dock", "continue", "resume", "travel", "navigate"]);

        return intent switch
        {
            TravelAutopilotActionIntent.SelectWaypoint => SelectWaypoint(context, package, memoryState, regions, preprocessing, ocr, templates, actionRegions, waypointTerms, minConfidence, allowRegionFallback),
            TravelAutopilotActionIntent.ToggleAutopilot => SelectToggleAutopilot(context, package, memoryState, regions, preprocessing, ocr, templates, actionRegions, autopilotTerms, minConfidence, allowRegionFallback),
            TravelAutopilotActionIntent.InvokeTravelControl => SelectTravelControl(context, package, memoryState, regions, preprocessing, ocr, templates, actionRegions, controlTerms, minConfidence, allowRegionFallback),
            _ => null
        };
    }

    private static TravelAutopilotActionSelection? SelectWaypoint(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        SessionScreenRegionResolution regions,
        SessionFramePreprocessingResult preprocessing,
        SessionOcrExtractionResult ocr,
        SessionTemplateDetectionResult templates,
        IReadOnlyList<string> actionRegions,
        IReadOnlyList<string> terms,
        double minConfidence,
        bool allowRegionFallback)
    {
        var nextWaypoint = package.TravelRoute.NextWaypointLabel ?? package.TravelRoute.VisibleWaypoints.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(nextWaypoint))
        {
            return null;
        }

        var line = FindBestOcrLine(ocr, actionRegions, nextWaypoint, terms);
        if (line?.Bounds is not null)
        {
            var confidence = ResolveConfidence(line.Confidence, FindArtifactConfidence(ocr, line.SourceArtifactName), 0.8d);
            if (confidence < minConfidence)
            {
                return null;
            }

            return BuildSelection(
                context,
                package,
                memoryState,
                TravelAutopilotActionIntent.SelectWaypoint,
                UiCommandKind.SelectItem,
                "ocr-line-click",
                line.SourceRegionName,
                line.SourceArtifactName,
                line.Text,
                line.Bounds,
                GetSourceSequence(ocr, preprocessing, templates, regions),
                confidence,
                "ocr",
                $"Matched waypoint '{nextWaypoint}' from OCR line evidence.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["matchType"] = "waypoint",
                    ["lineConfidence"] = (line.Confidence ?? confidence).ToString(CultureInfo.InvariantCulture)
                },
                selectedValue: nextWaypoint,
                actionCode: BehaviorReasonCodes.PlanSelectWaypoint,
                reasonCode: BehaviorReasonCodes.PlanSelectWaypoint,
                reason: $"Selected next waypoint '{nextWaypoint}' from screen evidence.");
        }

        if (allowRegionFallback)
        {
            var region = FindRegionFallback(regions, actionRegions);
            if (region?.Bounds is not null && region.Confidence >= minConfidence)
            {
                return BuildSelection(
                    context,
                    package,
                    memoryState,
                    TravelAutopilotActionIntent.SelectWaypoint,
                    UiCommandKind.SelectItem,
                    "region-center-fallback",
                    region.RegionName,
                    null,
                    nextWaypoint,
                    region.Bounds,
                    regions.SourceSnapshotSequence,
                    region.Confidence,
                    "region",
                    $"Falling back to region '{region.RegionName}' for waypoint selection.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["regionKind"] = region.RegionKind,
                        ["matchState"] = region.MatchState.ToString()
                    },
                    selectedValue: nextWaypoint,
                    actionCode: BehaviorReasonCodes.PlanSelectWaypoint,
                    reasonCode: BehaviorReasonCodes.PlanSelectWaypoint,
                    reason: $"Selected next waypoint '{nextWaypoint}' from screen evidence.");
            }
        }

        return null;
    }

    private static TravelAutopilotActionSelection? SelectToggleAutopilot(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        SessionScreenRegionResolution regions,
        SessionFramePreprocessingResult preprocessing,
        SessionOcrExtractionResult ocr,
        SessionTemplateDetectionResult templates,
        IReadOnlyList<string> actionRegions,
        IReadOnlyList<string> terms,
        double minConfidence,
        bool allowRegionFallback)
    {
        var line = FindBestOcrLine(ocr, actionRegions, null, terms);
        if (line?.Bounds is not null)
        {
            var confidence = ResolveConfidence(line.Confidence, FindArtifactConfidence(ocr, line.SourceArtifactName), 0.75d);
            if (confidence >= minConfidence)
            {
                return BuildSelection(
                    context,
                    package,
                    memoryState,
                    TravelAutopilotActionIntent.ToggleAutopilot,
                    UiCommandKind.ToggleNode,
                    "ocr-line-click",
                    line.SourceRegionName,
                    line.SourceArtifactName,
                    line.Text,
                    line.Bounds,
                    GetSourceSequence(ocr, preprocessing, templates, regions),
                    confidence,
                    "ocr",
                    "Matched an autopilot toggle label in OCR evidence.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["matchType"] = "autopilot-toggle",
                        ["lineConfidence"] = (line.Confidence ?? confidence).ToString(CultureInfo.InvariantCulture)
                    },
                    actionCode: BehaviorReasonCodes.PlanToggleAutopilot,
                    reasonCode: BehaviorReasonCodes.PlanToggleAutopilot,
                    reason: "Selected autopilot control from screen evidence.");
            }
        }

        var templateMatch = FindBestTemplateMatch(templates, terms);
        if (templateMatch?.Bounds is not null && templateMatch.Confidence >= minConfidence)
        {
            return BuildSelection(
                context,
                package,
                memoryState,
                TravelAutopilotActionIntent.ToggleAutopilot,
                UiCommandKind.ToggleNode,
                "template-click",
                templateMatch.SourceRegionName,
                templateMatch.SourceArtifactName,
                templateMatch.TemplateName,
                templateMatch.Bounds,
                templates.SourceSnapshotSequence,
                templateMatch.Confidence,
                "template",
                "Matched an autopilot toggle control in template evidence.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["templateKind"] = templateMatch.TemplateKind,
                    ["matchScore"] = templateMatch.MatchScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                },
                boolValue: true,
                actionCode: BehaviorReasonCodes.PlanToggleAutopilot,
                reasonCode: BehaviorReasonCodes.PlanToggleAutopilot,
                reason: "Selected autopilot control from screen evidence.");
        }

        if (allowRegionFallback)
        {
            var region = FindRegionFallback(regions, actionRegions);
            if (region?.Bounds is not null && region.Confidence >= minConfidence)
            {
                return BuildSelection(
                    context,
                    package,
                    memoryState,
                    TravelAutopilotActionIntent.ToggleAutopilot,
                    UiCommandKind.ToggleNode,
                    "region-center-fallback",
                    region.RegionName,
                    null,
                    "autopilot",
                    region.Bounds,
                    regions.SourceSnapshotSequence,
                    region.Confidence,
                    "region",
                    $"Falling back to region '{region.RegionName}' for autopilot toggle.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["regionKind"] = region.RegionKind,
                        ["matchState"] = region.MatchState.ToString()
                    },
                    boolValue: true,
                    actionCode: BehaviorReasonCodes.PlanToggleAutopilot,
                    reasonCode: BehaviorReasonCodes.PlanToggleAutopilot,
                    reason: "Selected autopilot control from screen evidence.");
            }
        }

        return null;
    }

    private static TravelAutopilotActionSelection? SelectTravelControl(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        SessionScreenRegionResolution regions,
        SessionFramePreprocessingResult preprocessing,
        SessionOcrExtractionResult ocr,
        SessionTemplateDetectionResult templates,
        IReadOnlyList<string> actionRegions,
        IReadOnlyList<string> terms,
        double minConfidence,
        bool allowRegionFallback)
    {
        var line = FindBestOcrLine(ocr, actionRegions, null, terms);
        if (line?.Bounds is not null)
        {
            var confidence = ResolveConfidence(line.Confidence, FindArtifactConfidence(ocr, line.SourceArtifactName), 0.75d);
            if (confidence >= minConfidence)
            {
                return BuildSelection(
                    context,
                    package,
                    memoryState,
                    TravelAutopilotActionIntent.InvokeTravelControl,
                    UiCommandKind.InvokeNodeAction,
                    "ocr-line-click",
                    line.SourceRegionName,
                    line.SourceArtifactName,
                    line.Text,
                    line.Bounds,
                    GetSourceSequence(ocr, preprocessing, templates, regions),
                    confidence,
                    "ocr",
                    "Matched a travel control label in OCR evidence.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["matchType"] = "travel-control",
                        ["lineConfidence"] = (line.Confidence ?? confidence).ToString(CultureInfo.InvariantCulture)
                    },
                    actionName: "default",
                    actionCode: BehaviorReasonCodes.PlanProgressAction,
                    reasonCode: BehaviorReasonCodes.PlanProgressAction,
                    reason: "Selected travel control from screen evidence.");
            }
        }

        var templateMatch = FindBestTemplateMatch(templates, terms);
        if (templateMatch?.Bounds is not null && templateMatch.Confidence >= minConfidence)
        {
            return BuildSelection(
                context,
                package,
                memoryState,
                TravelAutopilotActionIntent.InvokeTravelControl,
                UiCommandKind.InvokeNodeAction,
                "template-click",
                templateMatch.SourceRegionName,
                templateMatch.SourceArtifactName,
                templateMatch.TemplateName,
                templateMatch.Bounds,
                templates.SourceSnapshotSequence,
                templateMatch.Confidence,
                "template",
                "Matched a travel control in template evidence.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["templateKind"] = templateMatch.TemplateKind,
                    ["matchScore"] = templateMatch.MatchScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                },
                actionName: "default",
                actionCode: BehaviorReasonCodes.PlanProgressAction,
                reasonCode: BehaviorReasonCodes.PlanProgressAction,
                reason: "Selected travel control from screen evidence.");
        }

        if (allowRegionFallback)
        {
            var region = FindRegionFallback(regions, actionRegions);
            if (region?.Bounds is not null && region.Confidence >= minConfidence)
            {
                return BuildSelection(
                    context,
                    package,
                    memoryState,
                    TravelAutopilotActionIntent.InvokeTravelControl,
                    UiCommandKind.InvokeNodeAction,
                    "region-center-fallback",
                    region.RegionName,
                    null,
                    "travel-control",
                    region.Bounds,
                    regions.SourceSnapshotSequence,
                    region.Confidence,
                    "region",
                    $"Falling back to region '{region.RegionName}' for travel control invocation.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["regionKind"] = region.RegionKind,
                        ["matchState"] = region.MatchState.ToString()
                    },
                    actionName: "default",
                    actionCode: BehaviorReasonCodes.PlanProgressAction,
                    reasonCode: BehaviorReasonCodes.PlanProgressAction,
                    reason: "Selected travel control from screen evidence.");
            }
        }

        return null;
    }

    private static TravelAutopilotActionSelection BuildSelection(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        UiCommandKind commandKind,
        string actionKind,
        string? regionName,
        string? artifactName,
        string? candidateLabel,
        UiBounds relativeBounds,
        long sourceSnapshotSequence,
        double confidence,
        string evidenceSource,
        string explanation,
        IDictionary<string, string> diagnostics,
        string? actionName = null,
        string? selectedValue = null,
        bool? boolValue = null,
        string actionCode = "",
        string reasonCode = "",
        string reason = "")
    {
        var metadata = BuildMetadata(context, package, memoryState, intent, regionName, artifactName, candidateLabel, relativeBounds, sourceSnapshotSequence, confidence, evidenceSource, explanation, diagnostics, actionKind);
        metadata["uiCommandKind"] = commandKind.ToString();

        if (!string.IsNullOrWhiteSpace(actionName))
        {
            metadata["uiActionName"] = actionName!;
        }

        if (!string.IsNullOrWhiteSpace(selectedValue))
        {
            metadata["uiSelectedValue"] = selectedValue!;
        }

        if (boolValue is not null)
        {
            metadata["uiBoolValue"] = boolValue.Value.ToString();
        }

        var command = commandKind switch
        {
            UiCommandKind.SelectItem => new UiCommand(context.SessionSnapshot.SessionId, null, UiCommandKind.SelectItem, actionName: null, textValue: null, boolValue: null, selectedValue ?? candidateLabel, ToNullableMetadata(metadata)),
            UiCommandKind.ToggleNode => new UiCommand(context.SessionSnapshot.SessionId, null, UiCommandKind.ToggleNode, actionName: null, textValue: null, boolValue: boolValue ?? true, selectedValue: null, ToNullableMetadata(metadata)),
            UiCommandKind.InvokeNodeAction => new UiCommand(context.SessionSnapshot.SessionId, null, UiCommandKind.InvokeNodeAction, actionName, null, null, selectedValue, ToNullableMetadata(metadata)),
            _ => UiCommand.RefreshUi(context.SessionSnapshot.SessionId, metadata: ToNullableMetadata(metadata))
        };

        var screenTarget = new ScreenTravelActionTarget(
            intent,
            actionKind,
            new ScreenActionAnchor(regionName, artifactName, candidateLabel, relativeBounds, sourceSnapshotSequence, confidence, evidenceSource, explanation, new Dictionary<string, string>(diagnostics, StringComparer.Ordinal)));

        return new TravelAutopilotActionSelection(intent, command, actionCode, reasonCode, reason, metadata, screenTarget);
    }

    private static Dictionary<string, string> BuildMetadata(
        TargetBehaviorPlanningContext context,
        EveLikeSemanticPackageResult package,
        TravelAutopilotMemoryState memoryState,
        TravelAutopilotActionIntent intent,
        string? regionName,
        string? artifactName,
        string? candidateLabel,
        UiBounds relativeBounds,
        long sourceSnapshotSequence,
        double confidence,
        string evidenceSource,
        string explanation,
        IDictionary<string, string> diagnostics,
        string actionKind)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["behaviorPack"] = package.PackageName,
            ["behaviorPackVersion"] = package.PackageVersion,
            ["travelActionIntent"] = intent.ToString(),
            ["screenTravelIntent"] = intent.ToString(),
            ["screenActionKind"] = actionKind,
            ["screenEvidenceSource"] = evidenceSource,
            ["screenRegionName"] = regionName ?? string.Empty,
            ["screenArtifactName"] = artifactName ?? string.Empty,
            ["screenCandidateLabel"] = candidateLabel ?? string.Empty,
            ["screenRelativeBounds"] = FormatBounds(relativeBounds),
            ["screenSourceSnapshotSequence"] = sourceSnapshotSequence.ToString(CultureInfo.InvariantCulture),
            ["screenSelectionConfidence"] = confidence.ToString(CultureInfo.InvariantCulture),
            ["screenExplanation"] = explanation,
            ["routeFingerprint"] = ComputeRouteFingerprint(package)
        };

        if (!string.IsNullOrWhiteSpace(memoryState.LastActionCode))
        {
            metadata["lastActionCode"] = memoryState.LastActionCode!;
        }

        foreach (var (key, value) in diagnostics)
        {
            metadata[$"screenDiagnostic.{key}"] = value;
        }

        return metadata;
    }

    private static IReadOnlyDictionary<string, string?> ToNullableMetadata(IReadOnlyDictionary<string, string> metadata) =>
        metadata.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal);

    private static string FormatBounds(UiBounds bounds) =>
        string.Join(",", bounds.X.ToString(CultureInfo.InvariantCulture), bounds.Y.ToString(CultureInfo.InvariantCulture), bounds.Width.ToString(CultureInfo.InvariantCulture), bounds.Height.ToString(CultureInfo.InvariantCulture));

    private static ScreenRegionMatch? FindRegionFallback(SessionScreenRegionResolution regions, IReadOnlyList<string> regionNames)
    {
        if (regionNames.Count == 0)
        {
            return regions.Regions.OrderByDescending(static region => region.Confidence).FirstOrDefault(region => region.Bounds is not null && region.MatchState != ScreenRegionMatchState.Missing);
        }

        return regions.Regions
            .Where(region => region.Bounds is not null && region.MatchState != ScreenRegionMatchState.Missing)
            .Where(region => regionNames.Any(name => string.Equals(name, region.RegionName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(static region => region.Confidence)
            .FirstOrDefault();
    }

    private static OcrTextLine? FindBestOcrLine(
        SessionOcrExtractionResult ocr,
        IReadOnlyList<string> regionNames,
        string? exactLabel,
        IReadOnlyList<string> terms)
    {
        foreach (var artifact in ocr.Artifacts.OrderByDescending(static artifact => artifact.Confidence ?? 0d))
        {
            if (!string.IsNullOrWhiteSpace(artifact.SourceRegionName) && regionNames.Count > 0 && !regionNames.Any(region => string.Equals(region, artifact.SourceRegionName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var line = FindBestLineInArtifact(artifact, exactLabel, terms);
            if (line is not null)
            {
                return line;
            }
        }

        return null;
    }

    private static OcrTextLine? FindBestLineInArtifact(OcrArtifactResult artifact, string? exactLabel, IReadOnlyList<string> terms)
    {
        var lines = artifact.Lines.Where(static line => line.Bounds is not null).OrderByDescending(static line => line.Confidence ?? 0d).ToArray();
        if (lines.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(exactLabel))
        {
            var normalizedExact = Normalize(exactLabel);
            var exact = lines.FirstOrDefault(line => Normalize(line.Text).Contains(normalizedExact, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var term in terms)
        {
            var normalizedTerm = Normalize(term);
            var match = lines.FirstOrDefault(line => Normalize(line.Text).Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static TemplateMatch? FindBestTemplateMatch(SessionTemplateDetectionResult templates, IReadOnlyList<string> terms) =>
        templates.Artifacts
            .SelectMany(static artifact => artifact.Matches)
            .Where(match => terms.Any(term => MatchesTerm(match.TemplateName, term) || MatchesTerm(match.TemplateKind, term) || MatchesTerm(match.SourceArtifactName, term) || MatchesTerm(match.SourceRegionName, term)))
            .OrderByDescending(static match => match.Confidence)
            .FirstOrDefault();

    private static long GetSourceSequence(SessionOcrExtractionResult ocr, SessionFramePreprocessingResult preprocessing, SessionTemplateDetectionResult templates, SessionScreenRegionResolution regions) =>
        Math.Max(Math.Max(ocr.SourceSnapshotSequence, preprocessing.SourceSnapshotSequence), Math.Max(templates.SourceSnapshotSequence, regions.SourceSnapshotSequence));

    private static double? FindArtifactConfidence(SessionOcrExtractionResult ocr, string? sourceArtifactName)
    {
        if (string.IsNullOrWhiteSpace(sourceArtifactName))
        {
            return null;
        }

        return ocr.Artifacts.FirstOrDefault(artifact => string.Equals(artifact.ArtifactName, sourceArtifactName, StringComparison.OrdinalIgnoreCase))?.Confidence;
    }

    private static double ResolveConfidence(double? preferred, double? secondary, double fallback) =>
        preferred ?? secondary ?? fallback;

    private static bool MatchesTerm(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && Normalize(value!).Contains(Normalize(term), StringComparison.OrdinalIgnoreCase);

    private static bool ParseBoolMetadata(IReadOnlyDictionary<string, string?> metadata, string key, bool fallback) =>
        metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private static double ParseDoubleMetadata(IReadOnlyDictionary<string, string?> metadata, string key, double fallback) =>
        metadata.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string[] ParseCsvMetadata(IReadOnlyDictionary<string, string?> metadata, string key, IReadOnlyList<string> fallback)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback.ToArray();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            route.WaypointCount.ToString(CultureInfo.InvariantCulture),
            route.ProgressPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string Normalize(string value) =>
        new(value.Where(static ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '_' && ch != '/').Select(static ch => char.ToLowerInvariant(ch)).ToArray());
}
