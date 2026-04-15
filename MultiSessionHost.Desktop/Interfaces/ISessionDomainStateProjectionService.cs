using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface ISessionDomainStateProjectionService
{
    SessionDomainState Project(
        SessionDomainState current,
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionUiState? uiState,
        DesktopSessionAttachment? attachment,
        UiSemanticExtractionResult? semanticExtraction,
        DateTimeOffset now);

    SessionDomainState ProjectRefreshFailure(
        SessionDomainState current,
        Exception exception,
        DateTimeOffset now);
}
