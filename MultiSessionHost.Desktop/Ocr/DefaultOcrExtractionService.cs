using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Ocr;

public sealed class DefaultOcrExtractionService : IOcrExtractionService
{
    private const string DefaultProfileName = "DefaultRegionOcr";

    private readonly ISessionFramePreprocessingStore _preprocessingStore;
    private readonly ISessionOcrExtractionStore _ocrStore;
    private readonly IOcrEngine _ocrEngine;
    private readonly IClock _clock;
    private readonly ILogger<DefaultOcrExtractionService> _logger;

    public DefaultOcrExtractionService(
        ISessionFramePreprocessingStore preprocessingStore,
        ISessionOcrExtractionStore ocrStore,
        IOcrEngine ocrEngine,
        IClock clock,
        ILogger<DefaultOcrExtractionService> logger)
    {
        _preprocessingStore = preprocessingStore;
        _ocrStore = ocrStore;
        _ocrEngine = ocrEngine;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<SessionOcrExtractionResult?> ExtractLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken)
    {
        if (context.Target.Kind != DesktopTargetKind.ScreenCaptureDesktop)
        {
            return null;
        }

        var extractedAtUtc = _clock.UtcNow;
        var profile = BuildSelectionProfile(context.Target.Metadata);
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            var preprocessing = await _preprocessingStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (preprocessing is null)
            {
                errors.Add($"No frame preprocessing result is available for session '{sessionId}'.");
                var missingResult = BuildFailureResult(sessionId, context, extractedAtUtc, profile, warnings, errors);
                await _ocrStore.UpsertLatestAsync(sessionId, missingResult, cancellationToken).ConfigureAwait(false);
                return missingResult;
            }

            var selectedArtifacts = SelectArtifacts(preprocessing.Artifacts, profile, warnings);
            var artifactResults = new List<OcrArtifactResult>(selectedArtifacts.Count);

            foreach (var selected in selectedArtifacts)
            {
                var artifactResult = await ExtractArtifactAsync(selected, profile, cancellationToken).ConfigureAwait(false);
                artifactResults.Add(artifactResult);
            }

            var successfulArtifactCount = artifactResults.Count(static artifact => artifact.Errors.Count == 0);
            var failedArtifactCount = artifactResults.Count - successfulArtifactCount;
            var metadata = BuildResultMetadata(
                context,
                preprocessing,
                profile,
                _ocrEngine.EngineName,
                _ocrEngine.BackendName,
                artifactResults,
                warnings,
                errors);

            var result = new SessionOcrExtractionResult(
                sessionId,
                extractedAtUtc,
                preprocessing.SourceSnapshotSequence,
                preprocessing.SourceSnapshotCapturedAtUtc,
                preprocessing.SourceRegionResolutionSequence,
                preprocessing.SourceRegionResolutionResolvedAtUtc,
                preprocessing.ProcessedAtUtc,
                preprocessing.TargetKind,
                preprocessing.ObservabilityBackend,
                preprocessing.CaptureBackend,
                profile.ProfileName,
                _ocrEngine.EngineName,
                _ocrEngine.BackendName,
                artifactResults.Count,
                successfulArtifactCount,
                failedArtifactCount,
                artifactResults,
                warnings,
                errors,
                metadata);

            await _ocrStore.UpsertLatestAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OCR extraction failed for session '{SessionId}'.", sessionId);
            errors.Add(exception.Message);

            var failureResult = BuildFailureResult(sessionId, context, extractedAtUtc, profile, warnings, errors);
            await _ocrStore.UpsertLatestAsync(sessionId, failureResult, cancellationToken).ConfigureAwait(false);
            return failureResult;
        }
    }

    private async ValueTask<OcrArtifactResult> ExtractArtifactAsync(
        SelectedArtifact selected,
        OcrArtifactSelectionProfile profile,
        CancellationToken cancellationToken)
    {
        var artifactWarnings = new List<string>();
        var artifactErrors = new List<string>();

        try
        {
            var engineResult = await _ocrEngine.ExtractAsync(selected.Artifact, cancellationToken).ConfigureAwait(false);
            artifactWarnings.AddRange(engineResult.Warnings);
            artifactErrors.AddRange(engineResult.Errors);

            var fragments = engineResult.Fragments
                .Select(fragment => new OcrTextFragment(
                    fragment.Text,
                    NormalizeText(fragment.NormalizedText ?? fragment.Text),
                    fragment.Confidence,
                    fragment.Bounds,
                    selected.Artifact.ArtifactName,
                    selected.Artifact.SourceRegionName))
                .ToArray();

            var lines = engineResult.Lines
                .Select(line => new OcrTextLine(
                    line.Text,
                    NormalizeText(line.NormalizedText ?? line.Text),
                    line.Confidence,
                    line.Bounds,
                    selected.Artifact.ArtifactName,
                    selected.Artifact.SourceRegionName))
                .ToArray();

            var recognizedText = BuildRecognizedText(lines, fragments);
            var normalizedText = NormalizeText(recognizedText);
            var confidence = BuildConfidence(engineResult.Confidence, lines, fragments);

            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                artifactWarnings.Add("OCR produced empty text.");
            }

            var metadata = new Dictionary<string, string?>(selected.Artifact.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["selectedByStrategy"] = selected.SelectionStrategy,
                ["usedFullFrameFallback"] = selected.UsedFullFrameFallback.ToString(),
                ["ocrEngine"] = _ocrEngine.EngineName,
                ["ocrBackend"] = _ocrEngine.BackendName,
                ["isEmptyText"] = string.IsNullOrWhiteSpace(recognizedText).ToString()
            };

            foreach (var (key, value) in engineResult.Metadata)
            {
                metadata[key] = value;
            }

            return new OcrArtifactResult(
                selected.Artifact.ArtifactName,
                selected.Artifact.SourceRegionName,
                selected.Artifact.ArtifactKind,
                selected.Artifact.PreprocessingSteps,
                recognizedText,
                normalizedText,
                confidence,
                fragments.Length,
                lines.Length,
                selected.SelectionStrategy,
                selected.UsedFullFrameFallback,
                fragments,
                lines,
                artifactWarnings,
                artifactErrors,
                metadata);
        }
        catch (Exception exception)
        {
            var metadata = new Dictionary<string, string?>(selected.Artifact.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["selectedByStrategy"] = selected.SelectionStrategy,
                ["usedFullFrameFallback"] = selected.UsedFullFrameFallback.ToString(),
                ["ocrEngine"] = _ocrEngine.EngineName,
                ["ocrBackend"] = _ocrEngine.BackendName
            };

            return new OcrArtifactResult(
                selected.Artifact.ArtifactName,
                selected.Artifact.SourceRegionName,
                selected.Artifact.ArtifactKind,
                selected.Artifact.PreprocessingSteps,
                string.Empty,
                string.Empty,
                null,
                0,
                0,
                selected.SelectionStrategy,
                selected.UsedFullFrameFallback,
                [],
                [],
                artifactWarnings,
                [exception.Message],
                metadata);
        }
    }

    private static OcrArtifactSelectionProfile BuildSelectionProfile(IReadOnlyDictionary<string, string?> metadata)
    {
        var selectedProfileName = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.OcrProfile, DefaultProfileName).Trim();
        var defaults = string.Equals(selectedProfileName, OcrArtifactSelectionProfile.DefaultRegionOcrWithFullFrameFallback.ProfileName, StringComparison.OrdinalIgnoreCase)
            ? OcrArtifactSelectionProfile.DefaultRegionOcrWithFullFrameFallback
            : OcrArtifactSelectionProfile.DefaultRegionOcr;

        var regionSet = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.OcrRegionSet, string.Join(',', defaults.RegionNames));
        var preferredKinds = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.OcrPreferredArtifactKinds, string.Join(',', defaults.PreferredArtifactKinds));
        var includeFullFrameFallback = TryGetBoolean(metadata, DesktopTargetMetadata.OcrIncludeFullFrameFallback) ?? defaults.IncludeFullFrameFallback;

        var regionNames = ParseDistinctList(regionSet, defaults.RegionNames);
        var preferredArtifactKinds = ParseDistinctList(preferredKinds, defaults.PreferredArtifactKinds);

        return defaults with
        {
            ProfileName = string.IsNullOrWhiteSpace(selectedProfileName) ? defaults.ProfileName : selectedProfileName,
            RegionNames = regionNames,
            PreferredArtifactKinds = preferredArtifactKinds,
            IncludeFullFrameFallback = includeFullFrameFallback
        };
    }

    private static List<SelectedArtifact> SelectArtifacts(
        IReadOnlyList<ProcessedFrameArtifact> artifacts,
        OcrArtifactSelectionProfile profile,
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
                warnings.Add($"OCR region '{regionName}' was requested but no preprocessed region artifact is available.");
                continue;
            }

            var ordered = OrderByPreferredKinds(candidates, profile.PreferredArtifactKinds).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            if (profile.MergeMultipleAttempts)
            {
                foreach (var artifact in ordered.Take(2))
                {
                    selected.Add(new SelectedArtifact(artifact, profile.StrategyName, UsedFullFrameFallback: false));
                }
            }
            else
            {
                selected.Add(new SelectedArtifact(ordered[0], profile.StrategyName, UsedFullFrameFallback: false));
            }
        }

        if (selected.Count > 0)
        {
            return selected;
        }

        if (!profile.IncludeFullFrameFallback)
        {
            warnings.Add("No eligible region artifacts were selected for OCR.");
            return selected;
        }

        var fullFrameCandidates = usableArtifacts
            .Where(static artifact => string.IsNullOrWhiteSpace(artifact.SourceRegionName))
            .ToArray();

        var selectedFallback = OrderByPreferredKinds(fullFrameCandidates, profile.PreferredArtifactKinds).FirstOrDefault();
        if (selectedFallback is null)
        {
            warnings.Add("OCR full-frame fallback was enabled but no eligible full-frame artifact is available.");
            return selected;
        }

        selected.Add(new SelectedArtifact(selectedFallback, profile.StrategyName, UsedFullFrameFallback: true));
        warnings.Add("OCR used full-frame fallback because region artifact selection yielded no candidates.");

        return selected;
    }

    private static SessionOcrExtractionResult BuildFailureResult(
        SessionId sessionId,
        ResolvedDesktopTargetContext context,
        DateTimeOffset extractedAtUtc,
        OcrArtifactSelectionProfile profile,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var metadata = new Dictionary<string, string?>(context.Target.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["ocrProfile"] = profile.ProfileName,
            ["artifactSelectionStrategy"] = profile.StrategyName,
            ["targetKind"] = context.Target.Kind.ToString(),
            ["profileName"] = context.Profile.ProfileName,
            ["ocrEngine"] = "Unavailable",
            ["ocrBackend"] = "Unavailable",
            ["hasFailure"] = true.ToString()
        };

        return new SessionOcrExtractionResult(
            sessionId,
            extractedAtUtc,
            0,
            default,
            null,
            null,
            null,
            context.Target.Kind,
            DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.ObservabilityBackend, "ScreenCapture"),
            null,
            profile.ProfileName,
            "Unavailable",
            "Unavailable",
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
        OcrArtifactSelectionProfile profile,
        string ocrEngineName,
        string ocrBackendName,
        IReadOnlyList<OcrArtifactResult> artifacts,
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
        metadata["ocrProfile"] = profile.ProfileName;
        metadata["artifactSelectionStrategy"] = profile.StrategyName;
        metadata["ocrEngine"] = ocrEngineName;
        metadata["ocrBackend"] = ocrBackendName;
        metadata["attemptedArtifactCount"] = artifacts.Count.ToString();
        metadata["successfulArtifactCount"] = artifacts.Count(static artifact => artifact.Errors.Count == 0).ToString();
        metadata["failedArtifactCount"] = artifacts.Count(static artifact => artifact.Errors.Count > 0).ToString();
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

    private static string BuildRecognizedText(IReadOnlyList<OcrTextLine> lines, IReadOnlyList<OcrTextFragment> fragments)
    {
        if (lines.Count > 0)
        {
            return string.Join(Environment.NewLine, lines.Select(static line => line.Text));
        }

        if (fragments.Count > 0)
        {
            return string.Join(' ', fragments.Select(static fragment => fragment.Text));
        }

        return string.Empty;
    }

    private static double? BuildConfidence(double? engineConfidence, IReadOnlyList<OcrTextLine> lines, IReadOnlyList<OcrTextFragment> fragments)
    {
        if (engineConfidence.HasValue)
        {
            return engineConfidence;
        }

        var values = lines
            .Select(static line => line.Confidence)
            .Concat(fragments.Select(static fragment => fragment.Confidence))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Average();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new System.Text.StringBuilder(normalized.Length);
        var previousWasWhitespace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasWhitespace = true;
            }
            else
            {
                builder.Append(character);
                previousWasWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed record SelectedArtifact(
        ProcessedFrameArtifact Artifact,
        string SelectionStrategy,
        bool UsedFullFrameFallback);
}
