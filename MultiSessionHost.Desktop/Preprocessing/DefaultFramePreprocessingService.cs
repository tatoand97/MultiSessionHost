using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Regions;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Preprocessing;

public sealed class DefaultFramePreprocessingService : IFramePreprocessingService
{
    private const string DefaultProfileName = "DefaultFramePreprocessing";
    private const string DefaultImageFormat = "image/png";

    private readonly ISessionScreenSnapshotStore _screenSnapshotStore;
    private readonly ISessionScreenRegionStore _screenRegionStore;
    private readonly ISessionFramePreprocessingStore _preprocessingStore;
    private readonly IClock _clock;
    private readonly ILogger<DefaultFramePreprocessingService> _logger;

    public DefaultFramePreprocessingService(
        ISessionScreenSnapshotStore screenSnapshotStore,
        ISessionScreenRegionStore screenRegionStore,
        ISessionFramePreprocessingStore preprocessingStore,
        IClock clock,
        ILogger<DefaultFramePreprocessingService> logger)
    {
        _screenSnapshotStore = screenSnapshotStore;
        _screenRegionStore = screenRegionStore;
        _preprocessingStore = preprocessingStore;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<SessionFramePreprocessingResult?> PreprocessLatestAsync(SessionId sessionId, ResolvedDesktopTargetContext context, CancellationToken cancellationToken)
    {
        if (context.Target.Kind != DesktopTargetKind.ScreenCaptureDesktop)
        {
            return null;
        }

        var processedAtUtc = _clock.UtcNow;
        var selection = BuildArtifactSelection(context.Target.Metadata);
        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            var snapshot = await _screenSnapshotStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                errors.Add($"No screen snapshot is available for session '{sessionId}'.");
                var missingSnapshotResult = BuildFailureResult(sessionId, context, processedAtUtc, selection, warnings, errors);
                await _preprocessingStore.UpsertLatestAsync(sessionId, missingSnapshotResult, cancellationToken).ConfigureAwait(false);
                return missingSnapshotResult;
            }

            var regionResolution = await _screenRegionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var regionByName = regionResolution?.Regions
                .ToDictionary(static region => region.RegionName, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, ScreenRegionMatch>(StringComparer.OrdinalIgnoreCase);

            if (regionResolution is null)
            {
                warnings.Add("No screen region resolution is available; frame preprocessing was limited to full-frame artifacts.");
            }

            var artifactSpecs = BuildArtifactSpecs(selection);
            var artifacts = new List<ProcessedFrameArtifact>(artifactSpecs.Count);
            Bitmap? sourceBitmap = null;

            try
            {
                foreach (var artifactSpec in artifactSpecs)
                {
                    var artifact = BuildArtifact(
                        artifactSpec,
                        snapshot,
                        regionByName,
                        selection,
                        warnings,
                        ref sourceBitmap);
                    artifacts.Add(artifact);
                }
            }
            finally
            {
                sourceBitmap?.Dispose();
            }

            var successfulArtifactCount = artifacts.Count(static artifact => artifact.Errors.Count == 0);
            var failedArtifactCount = artifacts.Count - successfulArtifactCount;
            var metadata = BuildResultMetadata(snapshot, context, selection, regionResolution, warnings, errors);

            var result = new SessionFramePreprocessingResult(
                sessionId,
                processedAtUtc,
                snapshot.Sequence,
                snapshot.CapturedAtUtc,
                regionResolution?.SourceSnapshotSequence,
                regionResolution?.ResolvedAtUtc,
                snapshot.TargetKind,
                snapshot.ObservabilityBackend,
                snapshot.CaptureBackend,
                selection.ProfileName,
                artifacts.Count,
                successfulArtifactCount,
                failedArtifactCount,
                artifacts,
                warnings,
                errors,
                metadata);

            await _preprocessingStore.UpsertLatestAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Frame preprocessing failed for session '{SessionId}'.", sessionId);
            errors.Add(exception.Message);

            var failureResult = BuildFailureResult(sessionId, context, processedAtUtc, selection, warnings, errors);
            await _preprocessingStore.UpsertLatestAsync(sessionId, failureResult, cancellationToken).ConfigureAwait(false);
            return failureResult;
        }
    }

    private static FramePreprocessingArtifactSelection BuildArtifactSelection(IReadOnlyDictionary<string, string?> metadata)
    {
        var defaults = FramePreprocessingArtifactSelection.Default;
        var profileName = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.FramePreprocessingProfile, defaults.ProfileName);
        var regionSet = DesktopTargetMetadata.GetValue(metadata, DesktopTargetMetadata.FramePreprocessingRegionSet, string.Join(',', defaults.RegionNames));
        var includeThreshold = TryGetBoolean(metadata, DesktopTargetMetadata.FramePreprocessingIncludeThreshold) ?? defaults.IncludeThreshold;

        var regionNames = regionSet
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static regionName => !string.IsNullOrWhiteSpace(regionName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FramePreprocessingArtifactSelection(
            string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName.Trim(),
            regionNames.Length == 0 ? defaults.RegionNames : regionNames,
            includeThreshold,
            defaults.StrategyName);
    }

    private static List<ArtifactSpec> BuildArtifactSpecs(FramePreprocessingArtifactSelection selection)
    {
        var specs = new List<ArtifactSpec>
        {
            new("frame.raw", "raw", null, ArtifactOperation.Raw, ["passthrough"]),
            new("frame.grayscale", "grayscale", null, ArtifactOperation.Grayscale, ["grayscale"]),
            new("frame.high-contrast", "high-contrast", null, ArtifactOperation.HighContrast, ["grayscale", "contrast-normalization"])
        };

        if (selection.IncludeThreshold)
        {
            specs.Add(new ArtifactSpec("frame.threshold", "threshold", null, ArtifactOperation.Threshold, ["grayscale", "binary-threshold"]));
        }

        foreach (var regionName in selection.RegionNames)
        {
            specs.Add(new ArtifactSpec($"region:{regionName}.raw", "raw", regionName, ArtifactOperation.Raw, [$"crop:{regionName}"]));
            specs.Add(new ArtifactSpec($"region:{regionName}.grayscale", "grayscale", regionName, ArtifactOperation.Grayscale, [$"crop:{regionName}", "grayscale"]));
            specs.Add(new ArtifactSpec($"region:{regionName}.high-contrast", "high-contrast", regionName, ArtifactOperation.HighContrast, [$"crop:{regionName}", "grayscale", "contrast-normalization"]));

            if (selection.IncludeThreshold)
            {
                specs.Add(new ArtifactSpec($"region:{regionName}.threshold", "threshold", regionName, ArtifactOperation.Threshold, [$"crop:{regionName}", "grayscale", "binary-threshold"]));
            }
        }

        return specs;
    }

    private ProcessedFrameArtifact BuildArtifact(
        ArtifactSpec spec,
        SessionScreenSnapshot snapshot,
        IReadOnlyDictionary<string, ScreenRegionMatch> regionByName,
        FramePreprocessingArtifactSelection selection,
        List<string> resultWarnings,
        ref Bitmap? sourceBitmap)
    {
        try
        {
            if (spec.RegionName is null && spec.Operation == ArtifactOperation.Raw)
            {
                return new ProcessedFrameArtifact(
                    spec.ArtifactName,
                    spec.ArtifactKind,
                    snapshot.Sequence,
                    null,
                    snapshot.ImageWidth,
                    snapshot.ImageHeight,
                    snapshot.ImageFormat,
                    snapshot.ImageBytes.Length,
                    spec.Steps,
                    [],
                    [],
                    BuildArtifactMetadata(spec, selection, matchedRegion: null),
                    snapshot.ImageBytes.ToArray());
            }

            sourceBitmap ??= DecodeBitmap(snapshot.ImageBytes);

            var bounds = ResolveBounds(spec, sourceBitmap, regionByName, resultWarnings);
            var derivedBytes = BuildDerivedArtifactPayload(sourceBitmap, bounds, spec.Operation);
            return new ProcessedFrameArtifact(
                spec.ArtifactName,
                spec.ArtifactKind,
                snapshot.Sequence,
                spec.RegionName,
                bounds.Width,
                bounds.Height,
                DefaultImageFormat,
                derivedBytes.Length,
                spec.Steps,
                [],
                [],
                BuildArtifactMetadata(spec, selection, spec.RegionName),
                derivedBytes);
        }
        catch (Exception exception)
        {
            return new ProcessedFrameArtifact(
                spec.ArtifactName,
                spec.ArtifactKind,
                snapshot.Sequence,
                spec.RegionName,
                0,
                0,
                DefaultImageFormat,
                0,
                spec.Steps,
                [],
                [exception.Message],
                BuildArtifactMetadata(spec, selection, spec.RegionName),
                []);
        }
    }

    private static Rectangle ResolveBounds(
        ArtifactSpec spec,
        Bitmap sourceBitmap,
        IReadOnlyDictionary<string, ScreenRegionMatch> regionByName,
        List<string> resultWarnings)
    {
        if (spec.RegionName is null)
        {
            return new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
        }

        if (!regionByName.TryGetValue(spec.RegionName, out var region))
        {
            throw new InvalidOperationException($"Requested region '{spec.RegionName}' is not available in the latest region resolution.");
        }

        if (region.Bounds is null)
        {
            throw new InvalidOperationException($"Requested region '{spec.RegionName}' does not contain bounds.");
        }

        var bounds = ClampBounds(region.Bounds, sourceBitmap.Width, sourceBitmap.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"Requested region '{spec.RegionName}' resolved to an empty crop area.");
        }

        if (bounds.Width != region.Bounds.Width || bounds.Height != region.Bounds.Height)
        {
            resultWarnings.Add($"Region '{spec.RegionName}' crop bounds were clamped to fit the source frame.");
        }

        return bounds;
    }

    private static byte[] BuildDerivedArtifactPayload(Bitmap sourceBitmap, Rectangle bounds, ArtifactOperation operation)
    {
        using var cropped = Crop(sourceBitmap, bounds);

        return operation switch
        {
            ArtifactOperation.Raw => EncodeAsPng(cropped),
            ArtifactOperation.Grayscale => EncodeAsPng(ToGrayscale(cropped)),
            ArtifactOperation.HighContrast => EncodeAsPng(ToHighContrast(cropped)),
            ArtifactOperation.Threshold => EncodeAsPng(ToThreshold(cropped, 128)),
            _ => throw new InvalidOperationException($"Unsupported frame preprocessing operation '{operation}'.")
        };
    }

    private static SessionFramePreprocessingResult BuildFailureResult(
        SessionId sessionId,
        ResolvedDesktopTargetContext context,
        DateTimeOffset processedAtUtc,
        FramePreprocessingArtifactSelection selection,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var metadata = new Dictionary<string, string?>(context.Target.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["preprocessingProfile"] = selection.ProfileName,
            ["artifactSelectionStrategy"] = selection.StrategyName,
            ["targetKind"] = context.Target.Kind.ToString(),
            ["profileName"] = context.Profile.ProfileName,
            ["hasFailure"] = true.ToString()
        };

        return new SessionFramePreprocessingResult(
            sessionId,
            processedAtUtc,
            0,
            default,
            null,
            null,
            context.Target.Kind,
            DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.ObservabilityBackend, "ScreenCapture"),
            null,
            selection.ProfileName,
            0,
            0,
            0,
            [],
            warnings,
            errors,
            metadata);
    }

    private static IReadOnlyDictionary<string, string?> BuildResultMetadata(
        SessionScreenSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        FramePreprocessingArtifactSelection selection,
        SessionScreenRegionResolution? regionResolution,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var metadata = new Dictionary<string, string?>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in context.Target.Metadata)
        {
            metadata[key] = value;
        }

        metadata["targetKind"] = context.Target.Kind.ToString();
        metadata["profileName"] = context.Profile.ProfileName;
        metadata["preprocessingProfile"] = selection.ProfileName;
        metadata["artifactSelectionStrategy"] = selection.StrategyName;
        metadata["regionResolutionUsed"] = (regionResolution is not null).ToString();
        metadata["warningCount"] = warnings.Count.ToString();
        metadata["errorCount"] = errors.Count.ToString();

        return metadata;
    }

    private static IReadOnlyDictionary<string, string?> BuildArtifactMetadata(ArtifactSpec spec, FramePreprocessingArtifactSelection selection, string? matchedRegion) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["artifactKind"] = spec.ArtifactKind,
            ["preprocessingProfile"] = selection.ProfileName,
            ["artifactSelectionStrategy"] = selection.StrategyName,
            ["region"] = matchedRegion,
            ["scope"] = spec.RegionName is null ? "frame" : "region"
        };

    private static Bitmap DecodeBitmap(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(image);
    }

    private static byte[] EncodeAsPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap Crop(Bitmap sourceBitmap, Rectangle bounds)
    {
        var target = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(target);
        graphics.DrawImage(
            sourceBitmap,
            new Rectangle(0, 0, target.Width, target.Height),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            GraphicsUnit.Pixel);

        return target;
    }

    private static Bitmap ToGrayscale(Bitmap sourceBitmap)
    {
        var output = new Bitmap(sourceBitmap.Width, sourceBitmap.Height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < sourceBitmap.Height; y++)
        {
            for (var x = 0; x < sourceBitmap.Width; x++)
            {
                var pixel = sourceBitmap.GetPixel(x, y);
                var intensity = (int)Math.Clamp(Math.Round((pixel.R * 0.299d) + (pixel.G * 0.587d) + (pixel.B * 0.114d)), 0d, 255d);
                output.SetPixel(x, y, Color.FromArgb(pixel.A, intensity, intensity, intensity));
            }
        }

        return output;
    }

    private static Bitmap ToHighContrast(Bitmap sourceBitmap)
    {
        using var grayscale = ToGrayscale(sourceBitmap);
        var output = new Bitmap(grayscale.Width, grayscale.Height, PixelFormat.Format32bppArgb);
        var min = byte.MaxValue;
        var max = byte.MinValue;

        for (var y = 0; y < grayscale.Height; y++)
        {
            for (var x = 0; x < grayscale.Width; x++)
            {
                var value = grayscale.GetPixel(x, y).R;
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }
        }

        if (max == min)
        {
            for (var y = 0; y < grayscale.Height; y++)
            {
                for (var x = 0; x < grayscale.Width; x++)
                {
                    var pixel = grayscale.GetPixel(x, y);
                    output.SetPixel(x, y, Color.FromArgb(pixel.A, pixel.R, pixel.R, pixel.R));
                }
            }

            return output;
        }

        for (var y = 0; y < grayscale.Height; y++)
        {
            for (var x = 0; x < grayscale.Width; x++)
            {
                var pixel = grayscale.GetPixel(x, y);
                var normalized = (pixel.R - min) * 255 / (max - min);
                output.SetPixel(x, y, Color.FromArgb(pixel.A, normalized, normalized, normalized));
            }
        }

        return output;
    }

    private static Bitmap ToThreshold(Bitmap sourceBitmap, byte threshold)
    {
        using var grayscale = ToGrayscale(sourceBitmap);
        var output = new Bitmap(grayscale.Width, grayscale.Height, PixelFormat.Format32bppArgb);

        for (var y = 0; y < grayscale.Height; y++)
        {
            for (var x = 0; x < grayscale.Width; x++)
            {
                var pixel = grayscale.GetPixel(x, y);
                var binary = pixel.R >= threshold ? byte.MaxValue : byte.MinValue;
                output.SetPixel(x, y, Color.FromArgb(pixel.A, binary, binary, binary));
            }
        }

        return output;
    }

    private static UiBounds ApplyInset(UiBounds bounds, int insetPx) =>
        new(
            bounds.X + insetPx,
            bounds.Y + insetPx,
            Math.Max(0, bounds.Width - (insetPx * 2)),
            Math.Max(0, bounds.Height - (insetPx * 2)));

    private static UiBounds ApplyPadding(UiBounds bounds, int paddingPx, int maxWidth, int maxHeight)
    {
        var x = Math.Max(0, bounds.X - paddingPx);
        var y = Math.Max(0, bounds.Y - paddingPx);
        var right = Math.Min(maxWidth, bounds.X + bounds.Width + paddingPx);
        var bottom = Math.Min(maxHeight, bounds.Y + bounds.Height + paddingPx);

        return new UiBounds(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static Bitmap Resize(Bitmap sourceBitmap, int width, int height)
    {
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(sourceBitmap, new Rectangle(0, 0, width, height));

        return resized;
    }

    private static Rectangle ClampBounds(UiBounds bounds, int maxWidth, int maxHeight)
    {
        var x = Math.Max(0, bounds.X);
        var y = Math.Max(0, bounds.Y);
        var right = Math.Min(maxWidth, bounds.X + bounds.Width);
        var bottom = Math.Min(maxHeight, bounds.Y + bounds.Height);

        return new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record ArtifactSpec(
        string ArtifactName,
        string ArtifactKind,
        string? RegionName,
        ArtifactOperation Operation,
        IReadOnlyList<string> Steps);

    private enum ArtifactOperation
    {
        Raw,
        Grayscale,
        HighContrast,
        Threshold
    }
}
