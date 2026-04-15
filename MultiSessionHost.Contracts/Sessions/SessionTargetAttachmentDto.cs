namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionTargetAttachmentDto(
    int ProcessId,
    string ProcessName,
    string? CommandLine,
    long MainWindowHandle,
    long WindowHandle,
    string WindowTitle,
    string? BaseAddress,
    DateTimeOffset AttachedAtUtc);
