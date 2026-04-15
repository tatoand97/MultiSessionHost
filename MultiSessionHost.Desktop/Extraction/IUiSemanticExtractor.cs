namespace MultiSessionHost.Desktop.Extraction;

public interface IUiSemanticExtractor
{
    ValueTask<UiSemanticExtractionContribution> ExtractAsync(UiSemanticExtractionContext context, CancellationToken cancellationToken);
}
