using System.Globalization;
using System.Text.RegularExpressions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Templates;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class EveLikeSemanticPackage : ITargetSemanticPackage
{
    public const string SemanticPackageName = "EveLike";
    public const string SemanticPackageVersion = "6.4.0";

    private static readonly string[] DefaultRouteRegions = ["window.right", "window.top", "window.center"];
    private static readonly string[] DefaultRouteHeaderTerms = ["route", "travel", "autopilot", "waypoint", "destination"];
    private static readonly string[] DefaultRouteIgnoreTerms = ["overview", "probe", "scanner", "local", "wallet", "market", "inventory", "cargo"];
    private static readonly string[] DefaultArtifactKindPriority = ["threshold", "high-contrast", "grayscale", "raw"];

    private readonly IUiTreeQueryService _query;
    private readonly ISessionScreenSnapshotStore? _screenSnapshotStore;
    private readonly ISessionScreenRegionStore? _screenRegionStore;
    private readonly ISessionFramePreprocessingStore? _preprocessingStore;
    private readonly ISessionOcrExtractionStore? _ocrStore;
    private readonly ISessionTemplateDetectionStore? _templateStore;

    public EveLikeSemanticPackage(
        IUiTreeQueryService query,
        ISessionScreenSnapshotStore? screenSnapshotStore = null,
        ISessionScreenRegionStore? screenRegionStore = null,
        ISessionFramePreprocessingStore? preprocessingStore = null,
        ISessionOcrExtractionStore? ocrStore = null,
        ISessionTemplateDetectionStore? templateStore = null)
    {
        _query = query;
        _screenSnapshotStore = screenSnapshotStore;
        _screenRegionStore = screenRegionStore;
        _preprocessingStore = preprocessingStore;
        _ocrStore = ocrStore;
        _templateStore = templateStore;
    }

    public string PackageName => SemanticPackageName;

    public string PackageVersion => SemanticPackageVersion;

    public async ValueTask<TargetSemanticPackageResult> ExtractAsync(TargetSemanticPackageContext context, CancellationToken cancellationToken)
    {
        var targetKind = context.SemanticContext.TargetContext.Target.Kind;
        if (targetKind == DesktopTargetKind.ScreenCaptureDesktop)
        {
            return await ExtractFromScreenAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return ExtractFromUiTree(context);
    }

    private TargetSemanticPackageResult ExtractFromUiTree(TargetSemanticPackageContext context)
    {
        var visibleNodes = _query.Flatten(context.SemanticContext.UiTree)
            .Where(static node => node.Visible)
            .ToArray();

        var insufficientObservabilityWarning = GetInsufficientObservabilityWarning(context.SemanticContext.UiTree);
        var insufficientObservability = insufficientObservabilityWarning is not null;

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

        if (insufficientObservabilityWarning is not null)
        {
            warnings.Add(insufficientObservabilityWarning);
        }

        if (insufficientObservability)
        {
            route = route with
            {
                RouteActive = false,
                Confidence = DetectionConfidence.Unknown,
                Reasons = route.Reasons.Append(insufficientObservabilityWarning!).Distinct(StringComparer.Ordinal).ToArray()
            };
            tactical = tactical with { Confidence = Downgrade(tactical.Confidence) };
            safety = safety with { Confidence = Downgrade(safety.Confidence) };
            presence = presence with { Confidence = Downgrade(presence.Confidence) };
            overview = overview.Select(entry => entry with { Confidence = Downgrade(entry.Confidence) }).ToArray();
            probes = probes.Select(entry => entry with { Confidence = Downgrade(entry.Confidence) }).ToArray();
        }

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

        var packageConfidence = insufficientObservability
            ? Downgrade(SummarizeConfidence(confidenceSummary.Values))
            : SummarizeConfidence(confidenceSummary.Values);

        return new TargetSemanticPackageResult(
            PackageName,
            PackageVersion,
            true,
            packageConfidence,
            package.Warnings,
            confidenceSummary,
            FailureReason: null,
            EveLike: package);
    }

    private async ValueTask<TargetSemanticPackageResult> ExtractFromScreenAsync(TargetSemanticPackageContext context, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var route = await ExtractRouteFromScreenAsync(context, warnings, cancellationToken).ConfigureAwait(false);

        var presence = new LocalPresenceSnapshot(
            IsVisible: false,
            PanelLabel: null,
            VisibleEntityCount: 0,
            TotalEntityCount: null,
            Entities: [],
            Confidence: DetectionConfidence.Unknown,
            Warnings: ["Presence semantics for screen-backed targets remain conservative in Phase 10.1."]);

        var overview = Array.Empty<OverviewEntrySemantic>();
        var probes = Array.Empty<ProbeScannerEntrySemantic>();
        var tactical = new TacticalSnapshot([], 0, [], [], [], DetectionConfidence.Unknown, ["Tactical semantics for screen-backed targets remain conservative in Phase 10.1."]);
        var safety = new SafetyLocationSemantic(false, null, false, false, false, null, DetectionConfidence.Unknown, ["Safety semantics for screen-backed targets remain conservative in Phase 10.1."]);

        warnings.AddRange(presence.Warnings);
        warnings.AddRange(tactical.Warnings);
        warnings.AddRange(safety.Reasons);

        var confidenceSummary = new Dictionary<string, DetectionConfidence>(StringComparer.Ordinal)
        {
            ["presence"] = presence.Confidence,
            ["route"] = route.Confidence,
            ["overview"] = DetectionConfidence.Unknown,
            ["probeScanner"] = DetectionConfidence.Unknown,
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

        return new TargetSemanticPackageResult(
            PackageName,
            PackageVersion,
            true,
            SummarizeScreenBackedPackageConfidence(route.Confidence),
            package.Warnings,
            confidenceSummary,
            FailureReason: null,
            EveLike: package);
    }

    private async ValueTask<TravelRouteSnapshot> ExtractRouteFromScreenAsync(
        TargetSemanticPackageContext context,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var metadata = context.SemanticContext.TargetContext.Target.Metadata;
        var routeRegions = ParseCsvMetadata(metadata, "EveLike.RouteRegionSet", DefaultRouteRegions);
        var routeHeaderTerms = ParseCsvMetadata(metadata, "EveLike.RouteHeaderTerms", DefaultRouteHeaderTerms);
        var routeIgnoreTerms = ParseCsvMetadata(metadata, "EveLike.RouteIgnoreTerms", DefaultRouteIgnoreTerms);
        var minWaypointsForActive = ParseIntMetadata(metadata, "EveLike.RouteMinWaypointsForActive", 2, minimum: 1, maximum: 8);
        var useTemplateSupport = ParseBoolMetadata(metadata, "EveLike.RouteUseTemplateSupport", defaultValue: true);

        if (_screenSnapshotStore is null || _screenRegionStore is null || _preprocessingStore is null || _ocrStore is null || _templateStore is null)
        {
            warnings.Add("Screen-backed route extraction could not run because one or more visual stores were not available.");
            return new TravelRouteSnapshot(false, null, null, null, 0, [], null, DetectionConfidence.Unknown, ["Visual evidence stores were unavailable."]);
        }

        var sessionId = context.SemanticContext.SessionId;
        var snapshot = await _screenSnapshotStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var regions = await _screenRegionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var preprocessing = await _preprocessingStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var ocr = await _ocrStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var templates = await _templateStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

        var reasons = new List<string>();

        if (snapshot is null)
        {
            warnings.Add("No screen snapshot was available for route extraction.");
        }

        if (regions is null)
        {
            warnings.Add("No screen region resolution was available for route extraction.");
        }

        if (preprocessing is null)
        {
            warnings.Add("No frame preprocessing result was available for route extraction.");
        }

        if (ocr is null)
        {
            warnings.Add("Route extraction could not assert route state because OCR evidence was unavailable.");
            return new TravelRouteSnapshot(false, null, null, null, 0, [], null, DetectionConfidence.Unknown, warnings.Distinct(StringComparer.Ordinal).ToArray());
        }

        if (snapshot is not null && ocr.SourceSnapshotSequence != snapshot.Sequence)
        {
            warnings.Add($"OCR data sequence {ocr.SourceSnapshotSequence} did not match latest screen snapshot sequence {snapshot.Sequence}; using OCR conservatively.");
        }

        if (preprocessing is not null && ocr.SourceSnapshotSequence != preprocessing.SourceSnapshotSequence)
        {
            warnings.Add("OCR source sequence did not match latest preprocessing source sequence.");
        }

        var matchedRegions = regions?.Regions
            .Where(static region => region.MatchState != ScreenRegionMatchState.Missing)
            .Select(static region => region.RegionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var prioritizedArtifacts = PrioritizeRouteArtifacts(ocr.Artifacts, routeRegions, matchedRegions);
        if (prioritizedArtifacts.Length == 0)
        {
            warnings.Add("No OCR artifacts were available for route-adjacent regions.");
            return new TravelRouteSnapshot(false, null, null, null, 0, [], null, DetectionConfidence.Unknown, warnings.Distinct(StringComparer.Ordinal).ToArray());
        }

        var routeLineCandidates = CollectRouteLineCandidates(prioritizedArtifacts, routeHeaderTerms, routeIgnoreTerms);

        var headerEvidence = routeLineCandidates
            .Where(candidate => candidate.IsHeader)
            .ToArray();

        if (headerEvidence.Length > 0)
        {
            var topHeader = headerEvidence[0];
            reasons.Add($"Route panel/header was detected in OCR region '{topHeader.RegionName ?? "full-frame"}'.");
        }

        var destination = ExtractField(routeLineCandidates, ["destination", "dest", "to"]);
        var nextWaypoint = ExtractField(routeLineCandidates, ["next waypoint", "next", "waypoint"]);
        var currentLocation = ExtractField(routeLineCandidates, ["current location", "current system", "current", "location", "system"]);
        var progressPercent = ExtractProgress(routeLineCandidates);

        var visibleWaypoints = ExtractWaypoints(routeLineCandidates, routeHeaderTerms, routeIgnoreTerms, destination, nextWaypoint, currentLocation);
        var waypointCount = visibleWaypoints.Length;

        if (waypointCount > 0)
        {
            reasons.Add("Visible waypoints were extracted from OCR artifacts.");
        }

        if (string.IsNullOrWhiteSpace(currentLocation))
        {
            warnings.Add("Current location was not emitted because no direct visual evidence was found.");
        }

        var templateSupportCount = 0;
        if (useTemplateSupport && templates is not null)
        {
            templateSupportCount = CountRouteSupportingTemplates(templates, routeRegions, routeHeaderTerms);
            if (templateSupportCount > 0)
            {
                reasons.Add($"Template detection provided {templateSupportCount} route-supporting matches.");
            }
        }

        var hasHeaderEvidence = headerEvidence.Length > 0;
        var hasWaypointEvidence = waypointCount >= minWaypointsForActive;
        var hasFieldEvidence = !string.IsNullOrWhiteSpace(destination) || !string.IsNullOrWhiteSpace(nextWaypoint);
        var hasTemplateSupport = templateSupportCount > 0;

        var routeActive = (hasHeaderEvidence && (waypointCount > 0 || hasFieldEvidence)) ||
            (hasWaypointEvidence && (hasFieldEvidence || hasTemplateSupport));

        if (!routeActive && (hasHeaderEvidence || waypointCount > 0 || hasFieldEvidence))
        {
            warnings.Add("Route was not asserted because only weak OCR/header evidence was present.");
        }

        if (string.IsNullOrWhiteSpace(nextWaypoint) && waypointCount > 0 && !string.IsNullOrWhiteSpace(destination))
        {
            nextWaypoint = visibleWaypoints.FirstOrDefault(waypoint => !string.Equals(waypoint, destination, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(nextWaypoint))
            {
                reasons.Add("Next waypoint was inferred from visible route waypoints.");
            }
        }

        var confidence = ComputeRouteConfidence(routeActive, hasHeaderEvidence, waypointCount, hasFieldEvidence, hasTemplateSupport);
        return new TravelRouteSnapshot(
            routeActive,
            destination,
            currentLocation,
            nextWaypoint,
            waypointCount,
            visibleWaypoints,
            progressPercent,
            confidence,
            reasons.Concat(warnings).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static DetectionConfidence SummarizeScreenBackedPackageConfidence(DetectionConfidence routeConfidence) =>
        routeConfidence switch
        {
            DetectionConfidence.High => DetectionConfidence.Medium,
            DetectionConfidence.Medium => DetectionConfidence.Low,
            DetectionConfidence.Low => DetectionConfidence.Low,
            _ => DetectionConfidence.Unknown
        };

    private static DetectionConfidence ComputeRouteConfidence(
        bool routeActive,
        bool hasHeaderEvidence,
        int waypointCount,
        bool hasFieldEvidence,
        bool hasTemplateSupport)
    {
        if (!routeActive)
        {
            return hasHeaderEvidence || waypointCount > 0 || hasFieldEvidence
                ? DetectionConfidence.Low
                : DetectionConfidence.Unknown;
        }

        if (hasHeaderEvidence && waypointCount >= 2 && (hasFieldEvidence || hasTemplateSupport))
        {
            return DetectionConfidence.High;
        }

        return DetectionConfidence.Medium;
    }

    private static string[] ParseCsvMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        IReadOnlyList<string> defaultValues)
    {
        if (!metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValues.ToArray();
        }

        var parsed = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? defaultValues.ToArray() : parsed;
    }

    private static int ParseIntMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (!metadata.TryGetValue(key, out var rawValue) || !int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, minimum, maximum);
    }

    private static bool ParseBoolMetadata(IReadOnlyDictionary<string, string?> metadata, string key, bool defaultValue) =>
        metadata.TryGetValue(key, out var rawValue) && bool.TryParse(rawValue, out var parsed)
            ? parsed
            : defaultValue;

    private static OcrArtifactResult[] PrioritizeRouteArtifacts(
        IReadOnlyList<OcrArtifactResult> artifacts,
        IReadOnlyList<string> routeRegions,
        IReadOnlyList<string> knownRegions)
    {
        static int ArtifactKindPriority(string sourceArtifactKind)
        {
            for (var index = 0; index < DefaultArtifactKindPriority.Length; index++)
            {
                if (string.Equals(sourceArtifactKind, DefaultArtifactKindPriority[index], StringComparison.OrdinalIgnoreCase))
                {
                    return DefaultArtifactKindPriority.Length - index;
                }
            }

            return 0;
        }

        var routeRegionSet = new HashSet<string>(routeRegions, StringComparer.OrdinalIgnoreCase);
        var knownRegionSet = new HashSet<string>(knownRegions, StringComparer.OrdinalIgnoreCase);

        return artifacts
            .Select(artifact =>
            {
                var inRouteRegion = artifact.SourceRegionName is not null && routeRegionSet.Contains(artifact.SourceRegionName);
                var inKnownRegion = artifact.SourceRegionName is not null && knownRegionSet.Contains(artifact.SourceRegionName);
                var regionScore = inRouteRegion ? 100 : (inKnownRegion ? 20 : 0);
                var confidenceScore = (int)Math.Round((artifact.Confidence ?? 0d) * 10d, MidpointRounding.AwayFromZero);
                return new
                {
                    Artifact = artifact,
                    Score = regionScore + (ArtifactKindPriority(artifact.SourceArtifactKind) * 10) + confidenceScore
                };
            })
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Artifact.ArtifactName, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Artifact)
            .ToArray();
    }

    private static RouteLineCandidate[] CollectRouteLineCandidates(
        IReadOnlyList<OcrArtifactResult> artifacts,
        IReadOnlyList<string> routeHeaderTerms,
        IReadOnlyList<string> routeIgnoreTerms)
    {
        var deduplicated = new Dictionary<string, RouteLineCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in artifacts)
        {
            var rawLines = artifact.Lines.Count > 0
                ? artifact.Lines.Select(static line => line.Text)
                : artifact.NormalizedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var rawLine in rawLines)
            {
                var normalized = NormalizeRouteLine(rawLine);
                if (string.IsNullOrWhiteSpace(normalized) || IsNoiseLine(normalized, routeIgnoreTerms))
                {
                    continue;
                }

                var candidate = new RouteLineCandidate(
                    rawLine.Trim(),
                    normalized,
                    artifact.SourceRegionName,
                    artifact.ArtifactName,
                    artifact.SourceArtifactKind,
                    artifact.Confidence ?? 0d,
                    ContainsAny(normalized, routeHeaderTerms),
                    artifact.SourceRegionName is not null);

                if (!deduplicated.TryGetValue(candidate.NormalizedLine, out var existing) || existing.Score < candidate.Score)
                {
                    deduplicated[candidate.NormalizedLine] = candidate;
                }
            }
        }

        return deduplicated.Values
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.NormalizedLine, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ExtractField(IReadOnlyList<RouteLineCandidate> lines, IReadOnlyList<string> prefixes)
    {
        foreach (var candidate in lines)
        {
            foreach (var prefix in prefixes)
            {
                var expression = $"^\\s*{Regex.Escape(prefix)}\\s*[:-]?\\s*(.+)$";
                var match = Regex.Match(candidate.NormalizedLine, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                var value = NormalizeWaypointLabel(match.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static double? ExtractProgress(IReadOnlyList<RouteLineCandidate> lines)
    {
        foreach (var candidate in lines)
        {
            if (!candidate.NormalizedLine.Contains('%'))
            {
                continue;
            }

            if (!SemanticParsing.ContainsAny(candidate.NormalizedLine, "progress", "route", "travel", "jumps", "jump"))
            {
                continue;
            }

            var match = Regex.Match(candidate.NormalizedLine, "(\\d{1,3})\\s*%", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return Math.Clamp(parsed, 0d, 100d);
            }
        }

        return null;
    }

    private static string[] ExtractWaypoints(
        IReadOnlyList<RouteLineCandidate> lines,
        IReadOnlyList<string> routeHeaderTerms,
        IReadOnlyList<string> routeIgnoreTerms,
        string? destination,
        string? nextWaypoint,
        string? currentLocation)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(destination))
        {
            excluded.Add(destination);
        }

        if (!string.IsNullOrWhiteSpace(nextWaypoint))
        {
            excluded.Add(nextWaypoint);
        }

        if (!string.IsNullOrWhiteSpace(currentLocation))
        {
            excluded.Add(currentLocation);
        }

        var waypoints = new List<string>();
        foreach (var candidate in lines)
        {
            if (!candidate.HasRegionEvidence)
            {
                continue;
            }

            var label = NormalizeWaypointLabel(candidate.NormalizedLine);
            if (!IsPotentialWaypointLabel(label, routeHeaderTerms, routeIgnoreTerms) || excluded.Contains(label))
            {
                continue;
            }

            waypoints.Add(label);
            if (waypoints.Count == 25)
            {
                break;
            }
        }

        return waypoints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int CountRouteSupportingTemplates(
        SessionTemplateDetectionResult templates,
        IReadOnlyList<string> routeRegions,
        IReadOnlyList<string> routeHeaderTerms)
    {
        var regionSet = new HashSet<string>(routeRegions, StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var artifact in templates.Artifacts)
        {
            foreach (var match in artifact.Matches)
            {
                if (match.SourceRegionName is not null && regionSet.Count > 0 && !regionSet.Contains(match.SourceRegionName))
                {
                    continue;
                }

                var searchable = string.Join(' ', new[]
                {
                    match.TemplateName,
                    match.TemplateKind,
                    artifact.ArtifactName,
                    artifact.SourceArtifactKind
                });

                if (ContainsAny(searchable, routeHeaderTerms) ||
                    SemanticParsing.ContainsAny(searchable, "autopilot", "jump", "stargate", "route"))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsPotentialWaypointLabel(string? label, IReadOnlyList<string> routeHeaderTerms, IReadOnlyList<string> routeIgnoreTerms)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length < 2 || label.Length > 48)
        {
            return false;
        }

        if (label.Contains(':') || label.Contains('%'))
        {
            return false;
        }

        if (ContainsAny(label, routeHeaderTerms) || ContainsAny(label, routeIgnoreTerms))
        {
            return false;
        }

        return Regex.IsMatch(label, "^[a-zA-Z0-9][a-zA-Z0-9 '\\-]+$", RegexOptions.CultureInvariant);
    }

    private static bool IsNoiseLine(string line, IReadOnlyList<string> routeIgnoreTerms)
    {
        if (line.Length <= 1 || line.All(static ch => char.IsPunctuation(ch) || char.IsWhiteSpace(ch)))
        {
            return true;
        }

        if (SemanticParsing.ContainsAny(line, "fps", "cpu", "gpu", "ms"))
        {
            return true;
        }

        return ContainsAny(line, routeIgnoreTerms);
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(value) || terms.Count == 0)
        {
            return false;
        }

        return terms.Any(term => !string.IsNullOrWhiteSpace(term) && value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRouteLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, "^[0-9]+[.)\\-\\s]+", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant);
        return normalized.Trim();
    }

    private static string NormalizeWaypointLabel(string? value)
    {
        var normalized = NormalizeRouteLine(value);
        normalized = Regex.Replace(normalized, "^[>\\-\\*]+", string.Empty, RegexOptions.CultureInvariant).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private sealed record RouteLineCandidate(
        string RawLine,
        string NormalizedLine,
        string? RegionName,
        string ArtifactName,
        string SourceArtifactKind,
        double Score,
        bool IsHeader,
        bool HasRegionEvidence);

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
        var hasStructuralRouteEvidence = routePanel is not null ||
            waypoints.Length > 0 ||
            !string.IsNullOrWhiteSpace(destination) ||
            !string.IsNullOrWhiteSpace(nextWaypoint);
        var titleOnlyRouteHint = !hasStructuralRouteEvidence &&
            ContainsCue(currentLocation, "route", "travel", "autopilot", "waypoint");
        var active = hasStructuralRouteEvidence;
        var progress = SemanticParsing.GetPercent(routePanel ?? visibleNodes.FirstOrDefault(), _query);
        var reasons = new List<string>();

        if (titleOnlyRouteHint)
        {
            warnings.Add("Route activity was not asserted because only root/title hints were available.");
        }

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
        var docked = ContainsCue(safeLabel, "dock", "station", "hangar") || ContainsCue(route.CurrentLocationLabel, "station", "dock", "hangar");
        var tethered = ContainsCue(safeLabel, "tether") || overview.Any(entry => entry.Warnings.Any(warning => warning.Contains("tether", StringComparison.OrdinalIgnoreCase)));
        var hideAvailable = ContainsCue(safeLabel, "hide", "safe", "cloak", "safe spot") || overview.Any(entry => ContainsCue(entry.Label, "safe", "hide"));
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

    private static bool ContainsCue(string? value, params string[] keywords) =>
        !string.IsNullOrWhiteSpace(value) && SemanticParsing.ContainsAny(value, keywords);

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

    private static string? GetInsufficientObservabilityWarning(UiTree tree)
    {
        var metadata = tree.Metadata.Properties;
        if (!metadata.TryGetValue("opaqueRoot", out var opaqueRootValue) ||
            !bool.TryParse(opaqueRootValue, out var opaqueRoot) ||
            !opaqueRoot)
        {
            return null;
        }

        if (tree.Root.Children.Count > 0)
        {
            return null;
        }

        return "Native target is UIA root-only; semantic extraction is based on insufficient observable structure.";
    }

    private static DetectionConfidence Downgrade(DetectionConfidence confidence) =>
        confidence switch
        {
            DetectionConfidence.High or DetectionConfidence.Medium => DetectionConfidence.Low,
            _ => confidence
        };

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