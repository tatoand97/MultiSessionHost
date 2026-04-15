namespace MultiSessionHost.Desktop.Extraction;

public interface IUiSemanticExtractionPipeline
{
    ValueTask<UiSemanticExtractionResult> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken);
}
