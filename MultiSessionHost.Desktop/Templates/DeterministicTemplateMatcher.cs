using System.Drawing;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Desktop.Preprocessing;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Templates;

public sealed class DeterministicTemplateMatcher : ITemplateMatcher
{
    public const string Name = "DeterministicTemplateMatcher";

    private readonly ILogger<DeterministicTemplateMatcher> _logger;

    public DeterministicTemplateMatcher(ILogger<DeterministicTemplateMatcher> logger)
    {
        _logger = logger;
    }

    public string MatcherName => Name;

    public string BackendName => "SAD-Grayscale";

    public ValueTask<TemplateMatcherArtifactResult> MatchAsync(
        ProcessedFrameArtifact artifact,
        IReadOnlyList<VisualTemplateDefinition> templates,
        CancellationToken cancellationToken)
    {
        if (templates.Count == 0)
        {
            return ValueTask.FromResult(new TemplateMatcherArtifactResult([], [], [], new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)));
        }

        var warnings = new List<string>();
        var errors = new List<string>();
        var matches = new List<TemplateMatcherMatch>();

        try
        {
            var source = DecodeToGrayscale(artifact.ImageBytes);
            foreach (var template in templates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (template.ImageBytes is null || template.ImageBytes.Length == 0)
                {
                    warnings.Add($"Template '{template.TemplateName}' does not have an image payload and was skipped.");
                    continue;
                }

                var candidate = DecodeToGrayscale(template.ImageBytes);
                if (candidate.Width > source.Width || candidate.Height > source.Height)
                {
                    warnings.Add($"Template '{template.TemplateName}' is larger than artifact '{artifact.ArtifactName}' and was skipped.");
                    continue;
                }

                var best = FindBestMatch(source, candidate);
                if (best.Confidence < template.MatchingThreshold)
                {
                    continue;
                }

                var metadata = new Dictionary<string, string?>(template.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["thresholdUsed"] = template.MatchingThreshold.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    ["rawScore"] = best.RawScore.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)
                };

                matches.Add(
                    new TemplateMatcherMatch(
                        template.TemplateName,
                        template.TemplateKind,
                        best.Confidence,
                        new UiBounds(best.X, best.Y, candidate.Width, candidate.Height),
                        best.RawScore,
                        metadata));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Template matching failed for artifact '{ArtifactName}'.", artifact.ArtifactName);
            errors.Add(exception.Message);
        }

        var resultMetadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["matcher"] = MatcherName,
            ["backend"] = BackendName,
            ["candidateTemplateCount"] = templates.Count.ToString(),
            ["matchCount"] = matches.Count.ToString()
        };

        return ValueTask.FromResult(new TemplateMatcherArtifactResult(matches, warnings, errors, resultMetadata));
    }

    private static GrayImage DecodeToGrayscale(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        using var bitmap = new Bitmap(image);

        var values = new byte[bitmap.Width * bitmap.Height];
        var index = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var intensity = (int)Math.Clamp(Math.Round((pixel.R * 0.299d) + (pixel.G * 0.587d) + (pixel.B * 0.114d)), 0d, 255d);
                values[index++] = (byte)intensity;
            }
        }

        return new GrayImage(bitmap.Width, bitmap.Height, values);
    }

    private static MatchCandidate FindBestMatch(GrayImage source, GrayImage template)
    {
        var bestX = 0;
        var bestY = 0;
        var bestRawScore = 0d;
        var bestConfidence = double.MinValue;
        var maxDiff = template.Width * template.Height * 255d;

        for (var y = 0; y <= source.Height - template.Height; y++)
        {
            for (var x = 0; x <= source.Width - template.Width; x++)
            {
                var diff = 0d;
                for (var ty = 0; ty < template.Height; ty++)
                {
                    var sourceRow = (y + ty) * source.Width;
                    var templateRow = ty * template.Width;
                    for (var tx = 0; tx < template.Width; tx++)
                    {
                        var sourceValue = source.Values[sourceRow + x + tx];
                        var templateValue = template.Values[templateRow + tx];
                        diff += Math.Abs(sourceValue - templateValue);
                    }
                }

                var confidence = 1d - (diff / maxDiff);
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestRawScore = confidence;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return new MatchCandidate(bestX, bestY, bestConfidence, bestRawScore);
    }

    private sealed record GrayImage(int Width, int Height, byte[] Values);

    private sealed record MatchCandidate(int X, int Y, double Confidence, double RawScore);
}
