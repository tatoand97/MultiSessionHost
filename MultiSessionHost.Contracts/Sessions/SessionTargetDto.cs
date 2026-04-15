namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionTargetDto(
    string SessionId,
    DesktopTargetProfileDto Profile,
    SessionTargetBindingDto Binding,
    ResolvedDesktopTargetDto Target,
    SessionTargetAttachmentDto? Attachment,
    string AdapterKind,
    string AdapterType);
