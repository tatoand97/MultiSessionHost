using MultiSessionHost.Desktop.Preprocessing;

namespace MultiSessionHost.Desktop.Ocr;

public sealed class NoOpOcrEngine : IOcrEngine
{
    public string EngineName => nameof(NoOpOcrEngine);

    public string BackendName => "none";

    public ValueTask<OcrEngineResult> ExtractAsync(ProcessedFrameArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return ExtractAsync(artifact.ImageBytes, artifact.ImageFormat, artifact.Metadata, cancellationToken);
    }

    public ValueTask<OcrEngineResult> ExtractAsync(
        byte[] imageBytes,
        string imageFormat,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken)
    {
        var result = new OcrEngineResult(
            [],
            [],
            null,
            ["No concrete OCR backend is configured. Empty OCR output was returned."],
            [],
            new Dictionary<string, string?>(metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["engineName"] = EngineName,
                ["backendName"] = BackendName
            });

        return ValueTask.FromResult(result);
    }
}
