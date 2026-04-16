using MultiSessionHost.Desktop.Preprocessing;

namespace MultiSessionHost.Desktop.Ocr;

public interface IOcrEngine
{
    string EngineName { get; }

    string BackendName { get; }

    ValueTask<OcrEngineResult> ExtractAsync(ProcessedFrameArtifact artifact, CancellationToken cancellationToken);

    ValueTask<OcrEngineResult> ExtractAsync(
        byte[] imageBytes,
        string imageFormat,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken);
}
