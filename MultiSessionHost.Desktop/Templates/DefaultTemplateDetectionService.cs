using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Ocr;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Templates;

public sealed class DefaultTemplateDetectionService : ITemplateDetectionService
{
    private const string DefaultProfileName = "DefaultRegionTemplateDetection";

    private readonly ISessionFramePreprocessingStore _preprocessingStore;
    private readonly ISessionOcrExtractionStore _ocrStore;
    private readonly ISessionTemplateDetectionStore _templateStore;
    private readonly IVisualTemplateRegistry _templateRegistry;
    private readonly ITemplateMatcher _templateMatcher;
    private readonly IClock _clock;
    private readonly ILogger<DefaultTemplateDetectionService> _logger;

    public DefaultTemplateDetectionService(
        ISessionFramePreprocessingStore preprocessingStore,
        ISessionOcrExtractionStore ocrStore,
        ISessionTemplateDetectionStore templateStore,
        IVisualTemplateRegistry templateRegistry,
        ITemplateMatcher templateMatcher,
        IClock clock,
        ILogger<DefaultTemplateDetectionService> logger)
    {
        _preprocessingStore = preprocessingStore;
        _ocrStore = ocrStore;
        _templateStore = templateStore;
        _templateRegistry = templateRegistry;
        _templateMatcher = templateMatcher;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<SessionTemplateDetectionResult?> DetectLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken)
    {
        if (context.Target.Kind != DesktopTargetKind.ScreenCaptureDesktop)
        {
            return null;
        }

        var detectedAtUtc = _clock.UtcNow;
        var profile = BuildProfile(context.Target.Metadata);
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            var preprocessing = await _preprocessingStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var latestOcr = await _ocrStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (preprocessing is null)
            {
                errors.Add($"No frame preprocessing result is available for session '{sessionId}'.");
                var missingResult = BuildFailureResult(sessionId, context, detectedAtUtc, latestOcr, profile, warnings, errors);
                await _templateStore.UpsertLatestAsync(sessionId, missingResult, cancellationToken).ConfigureAwait(false);
                return missingResult;
            }

            var templateSet = _templateRegistry.Resolve(context, profile);
            var selectedArtifacts = SelectArtifacts(preprocessing.Artifacts, profile, warnings);
            var artifactResults = new List<TemplateArtifactResult>(selectedArtifacts.Count);

            foreach (var selected in selectedArtifacts)
            {
                var artifactTemplates = SelectTemplatesForArtifact(templateSet.Templates, selected.Artifact, profile);
                var artifactResult = await DetectArtifactAsync(selected, artifactTemplates, profile, cancellationToken).ConfigureAwait(false);
                artifactResults.Add(artifactResult);
            }

            var successfulArtifactCount = artifactResults.Count(static artifact => artifact.Errors.Count == 0);
            var failedArtifactCount = artifactResults.Count - successfulArtifactCount;
            var totalTemplatesEvaluated = artifactResults.Sum(static artifact => artifact.EvaluatedTemplateCount);

            var metadata = BuildResultMetadata(
                context,
                preprocessing,
                latestOcr,
                profile,
                templateSet,
                _templateMatcher.MatcherName,
                _templateMatcher.BackendName,
                artifactResults,
                warnings,
                errors);

            var result = new SessionTemplateDetectionResult(
                sessionId,
                detectedAtUtc,
                preprocessing.SourceSnapshotSequence,
                preprocessing.SourceSnapshotCapturedAtUtc,
                preprocessing.SourceRegionResolutionSequence,
                preprocessing.SourceRegionResolutionResolvedAtUtc,
                preprocessing.ProcessedAtUtc,
                latestOcr?.ExtractedAtUtc,
                preprocessing.TargetKind,
                preprocessing.ObservabilityBackend,
                preprocessing.CaptureBackend,
                profile.ProfileName,
                templateSet.TemplateSetName,
                _templateMatcher.MatcherName,
                _templateMatcher.BackendName,
                artifactResults.Count,
                totalTemplatesEvaluated,
                successfulArtifactCount,
                failedArtifactCount,
                artifactResults,
                warnings,
                errors,
                metadata);

            await _templateStore.UpsertLatestAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Template detection failed for session '{SessionId}'.", sessionId);
            errors.Add(exception.Message);
            var latestOcr = await _ocrStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var failureResult = BuildFailureResult(sessionId, context, detectedAtUtc, latestOcr, profile, warnings, errors);
            await _templateStore.UpsertLatestAsync(sessionId, failureResult, cancellationToken).ConfigureAwait(false);
            return failureResult;
        }
    }

    private async ValueTask<TemplateArtifactResult> DetectArtifactAsync(
        SelectedArtifact selected,
        IReadOnlyList<VisualTemplateDefinition> templates,
        TemplateDetectionProfile profile,
        CancellationToken cancellationToken)
    {
        var artifactWarnings = new List<string>();
        var artifactErrors = new List<string>();

        try
        {
            var matcherResult = await _templateMatcher.MatchAsync(selected.Artifact, templates, cancellationToken).ConfigureAwait(false);
            artifactWarnings.AddRange(matcherResult.Warnings);
            artifactErrors.AddRange(matcherResult.Errors);

            var matches = matcherResult.Matches
                .Select(
                    match =>
                    {
                        var threshold = templates.FirstOrDefault(template => string.Equals(template.TemplateName, match.TemplateName, StringComparison.OrdinalIgnoreCase))?.MatchingThreshold ?? 0d;

                        var metadata = new Dictionary<string, string?>(match.Metadata, StringComparer.OrdinalIgnoreCase)
                        {
                            ["thresholdUsed"] = threshold.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                        };

                        return new TemplateMatch(
                            match.TemplateName,
                            match.TemplateKind,
                            match.Confidence,
                            match.Bounds,
                            selected.Artifact.ArtifactName,
                            selected.Artifact.SourceRegionName,
                            match.RawScore,
                            threshold,
                            metadata);
                    })
                .ToArray();

            var metadata = new Dictionary<string, string?>(selected.Artifact.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["selectedByStrategy"] = selected.SelectionStrategy,
                ["usedFullFrameFallback"] = selected.UsedFullFrameFallback.ToString(),
                ["matcher"] = _templateMatcher.MatcherName,
                ["backend"] = _templateMatcher.BackendName,
                ["templateProfile"] = profile.ProfileName,
                ["evaluatedTemplateCount"] = templates.Count.ToString(),
                ["matchedTemplateCount"] = matches.Length.ToString()
            };

            foreach (var (key, value) in matcherResult.Metadata)
            {
                metadata[key] = value;
            }

            return new TemplateArtifactResult(
                selected.Artifact.ArtifactName,
                selected.Artifact.SourceRegionName,
                selected.Artifact.ArtifactKind,
                selected.SelectionStrategy,
                selected.UsedFullFrameFallback,
                templates.Count,
                matches.Length,
                matches,
                artifactWarnings,
                artifactErrors,
                metadata);
        }
        catch (Exception exception)
        {
            return new TemplateArtifactResult(
                selected.Artifact.ArtifactName,
                selected.Artifact.SourceRegionName,
                selected.Artifact.ArtifactKind,
                selected.SelectionStrategy,
                selected.UsedFullFrameFallback,
                templates.Count,
                0,
                [],
                artifactWarnings,
                [exception.Message],
                new Dictionary<string, string?>(selected.Artifact.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["selectedByStrategy"] = selected.SelectionStrategy,
                    ["usedFullFrameFallback"] = selected.UsedFullFrameFallback.ToString(),
                    ["matcher"] = _templateMatcher.MatcherName,
                    ["backend"] = _templateMatcher.BackendName,
                    ["templateProfile"] = profile.ProfileName
                });
        }
    }

    private static TemplateDetectionProfile BuildProfile(IReadOnlyDictionary<string, string?> metadata)
    {
        var selectedProfileName = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.TemplateDetectionProfile, DefaultProfileName).Trim();
        var defaults = string.Equals(selectedProfileName, TemplateDetectionProfile.DefaultRegionTemplateDetectionWithFullFrameFallback.ProfileName, StringComparison.OrdinalIgnoreCase)
            ? TemplateDetectionProfile.DefaultRegionTemplateDetectionWithFullFrameFallback
            : TemplateDetectionProfile.DefaultRegionTemplateDetection;

        var regionSet = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.TemplateRegionSet, string.Join(',', defaults.RegionNames));
        var preferredKinds = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.TemplatePreferredArtifactKinds, string.Join(',', defaults.PreferredArtifactKinds));
        var templateSet = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.TemplateSet, defaults.TemplateSetName);
        var includeFullFrameFallback = TryGetBoolean(metadata, DesktopTargetMetadata.TemplateIncludeFullFrameFallback) ?? defaults.IncludeFullFrameFallback;

        var regionNames = ParseDistinctList(regionSet, defaults.RegionNames);
        var preferredArtifactKinds = ParseDistinctList(preferredKinds, defaults.PreferredArtifactKinds);

        return defaults with
        {
            ProfileName = string.IsNullOrWhiteSpace(selectedProfileName) ? defaults.ProfileName : selectedProfileName,
            RegionNames = regionNames,
            PreferredArtifactKinds = preferredArtifactKinds,
            IncludeFullFrameFallback = includeFullFrameFallback,
            TemplateSetName = string.IsNullOrWhiteSpace(templateSet) ? defaults.TemplateSetName : templateSet.Trim()
        };
    }

    private static List<SelectedArtifact> SelectArtifacts(
        IReadOnlyList<ProcessedFrameArtifact> artifacts,
        TemplateDetectionProfile profile,
        List<string> warnings)
    {
        var usableArtifacts = artifacts
            .Where(static artifact => artifact.Errors.Count == 0 && artifact.PayloadByteLength > 0)
            .ToArray();
        var selected = new List<SelectedArtifact>();

        var regionArtifacts = usableArtifacts
            .Where(static artifact => !string.IsNullOrWhiteSpace(artifact.SourceRegionName))
            .GroupBy(static artifact => artifact.SourceRegionName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var regionName in profile.RegionNames)
        {
            if (!regionArtifacts.TryGetValue(regionName, out var candidates))
            {
                warnings.Add($"Template region '{regionName}' was requested but no preprocessed region artifact is available.");
                continue;
            }

            var ordered = OrderByPreferredKinds(candidates, profile.PreferredArtifactKinds).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            selected.Add(new SelectedArtifact(ordered[0], profile.StrategyName, UsedFullFrameFallback: false));
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        if (!profile.IncludeFullFrameFallback)
        {
            warnings.Add("No eligible region artifacts were selected for template detection.");
            return selected;
        }

        var fullFrameCandidates = usableArtifacts
            .Where(static artifact => string.IsNullOrWhiteSpace(artifact.SourceRegionName))
            .ToArray();

        var selectedFallback = OrderByPreferredKinds(fullFrameCandidates, profile.PreferredArtifactKinds).FirstOrDefault();
        if (selectedFallback is null)
        {
            warnings.Add("Template full-frame fallback was enabled but no eligible full-frame artifact is available.");
            return selected;
        }

        selected.Add(new SelectedArtifact(selectedFallback, profile.StrategyName, UsedFullFrameFallback: true));
        warnings.Add("Template detection used full-frame fallback because region artifact selection yielded no candidates.");

        return selected;
    }

    private static IReadOnlyList<VisualTemplateDefinition> SelectTemplatesForArtifact(
        IReadOnlyList<VisualTemplateDefinition> templates,
        ProcessedFrameArtifact artifact,
        TemplateDetectionProfile profile)
    {
        return templates
            .Where(
                template =>
                    (template.ExpectedSourceArtifactKinds.Count == 0
                     || template.ExpectedSourceArtifactKinds.Any(kind => string.Equals(kind, artifact.ArtifactKind, StringComparison.OrdinalIgnoreCase)))
                    && (template.PreferredRegions.Count == 0
                        || (!string.IsNullOrWhiteSpace(artifact.SourceRegionName)
                            && template.PreferredRegions.Any(region => string.Equals(region, artifact.SourceRegionName, StringComparison.OrdinalIgnoreCase)))))
            .OrderBy(static template => template.TemplateName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SessionTemplateDetectionResult BuildFailureResult(
        SessionId sessionId,
        ResolvedDesktopTargetContext context,
        DateTimeOffset detectedAtUtc,
        SessionOcrExtractionResult? latestOcr,
        TemplateDetectionProfile profile,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var metadata = new Dictionary<string, string?>(context.Target.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["templateDetectionProfile"] = profile.ProfileName,
            ["artifactSelectionStrategy"] = profile.StrategyName,
            ["targetKind"] = context.Target.Kind.ToString(),
            ["profileName"] = context.Profile.ProfileName,
            ["matcher"] = "Unavailable",
            ["backend"] = "Unavailable",
            ["templateSet"] = profile.TemplateSetName,
            ["hasFailure"] = true.ToString()
        };

        return new SessionTemplateDetectionResult(
            sessionId,
            detectedAtUtc,
            0,
            default,
            null,
            null,
            null,
            latestOcr?.ExtractedAtUtc,
            context.Target.Kind,
            DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.ObservabilityBackend, "ScreenCapture"),
            null,
            profile.ProfileName,
            profile.TemplateSetName,
            "Unavailable",
            "Unavailable",
            0,
            0,
            0,
            0,
            [],
            warnings,
            errors,
            metadata);
    }

    private static IReadOnlyDictionary<string, string?> BuildResultMetadata(
        ResolvedDesktopTargetContext context,
        SessionFramePreprocessingResult preprocessing,
        SessionOcrExtractionResult? latestOcr,
        TemplateDetectionProfile profile,
        VisualTemplateSet templateSet,
        string matcherName,
        string matcherBackend,
        IReadOnlyList<TemplateArtifactResult> artifacts,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var metadata = new Dictionary<string, string?>(preprocessing.Metadata, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in context.Target.Metadata)
        {
            metadata[key] = value;
        }

        metadata["targetKind"] = context.Target.Kind.ToString();
        metadata["profileName"] = context.Profile.ProfileName;
        metadata["templateDetectionProfile"] = profile.ProfileName;
        metadata["artifactSelectionStrategy"] = profile.StrategyName;
        metadata["templateSet"] = templateSet.TemplateSetName;
        metadata["matcher"] = matcherName;
        metadata["backend"] = matcherBackend;
        metadata["attemptedArtifactCount"] = artifacts.Count.ToString();
        metadata["attemptedTemplateCount"] = artifacts.Sum(static artifact => artifact.EvaluatedTemplateCount).ToString();
        metadata["successfulArtifactCount"] = artifacts.Count(static artifact => artifact.Errors.Count == 0).ToString();
        metadata["failedArtifactCount"] = artifacts.Count(static artifact => artifact.Errors.Count > 0).ToString();
        metadata["sourceOcrExtractedAtUtc"] = latestOcr?.ExtractedAtUtc.ToString("O");
        metadata["warningCount"] = warnings.Count.ToString();
        metadata["errorCount"] = errors.Count.ToString();

        return metadata;
    }

    private static IEnumerable<ProcessedFrameArtifact> OrderByPreferredKinds(
        IEnumerable<ProcessedFrameArtifact> artifacts,
        IReadOnlyList<string> preferredKinds)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;

        return artifacts
            .OrderBy(
                artifact =>
                {
                    for (var i = 0; i < preferredKinds.Count; i++)
                    {
                        if (comparer.Equals(preferredKinds[i], artifact.ArtifactKind))
                        {
                            return i;
                        }
                    }

                    return int.MaxValue;
                })
            .ThenBy(static artifact => artifact.ArtifactName, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseDistinctList(string raw, IReadOnlyList<string> fallback)
    {
        var parsed = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? fallback : parsed;
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed record SelectedArtifact(ProcessedFrameArtifact Artifact, string SelectionStrategy, bool UsedFullFrameFallback);
}
