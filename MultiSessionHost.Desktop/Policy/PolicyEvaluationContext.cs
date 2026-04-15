using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

public sealed record PolicyEvaluationContext(
    SessionId SessionId,
    SessionSnapshot SessionSnapshot,
    SessionUiState? SessionUiState,
    SessionDomainState SessionDomainState,
    UiSemanticExtractionResult? UiSemanticExtractionResult,
    RiskAssessmentResult? RiskAssessmentResult,
    ResolvedDesktopTargetContext? ResolvedDesktopTargetContext,
    DesktopSessionAttachment? DesktopSessionAttachment,
    DateTimeOffset Now,
    PolicyMemoryContext? MemoryContext = null);
