using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record ExecutionLeaseMetadata(
    Guid ExecutionId,
    SessionId SessionId,
    ExecutionOperationKind OperationKind,
    SessionWorkItemKind? WorkItemKind,
    UiCommandKind? UiCommandKind,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset AcquiredAtUtc,
    TimeSpan WaitDuration,
    ExecutionResourceSet ResourceSet,
    string? Description);
