using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Interfaces;

public interface IExecutionResourceResolver
{
    ExecutionTargetIdentity CreateTargetIdentity(ResolvedDesktopTargetContext context);

    ExecutionTargetIdentity CreateTargetIdentity(DesktopSessionAttachment attachment);

    ExecutionRequest CreateForWorkItem(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionWorkItem workItem);

    ExecutionRequest CreateForUiCommand(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        UiCommand command);

    ExecutionRequest CreateForAttachmentEnsure(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context);

    ExecutionRequest CreateForAttachmentInvalidate(
        SessionId sessionId,
        DesktopSessionAttachment attachment,
        string? description = null);
}
