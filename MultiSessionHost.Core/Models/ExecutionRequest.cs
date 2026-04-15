using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record ExecutionRequest
{
    public ExecutionRequest(
        Guid executionId,
        SessionId sessionId,
        ExecutionOperationKind operationKind,
        SessionWorkItemKind? workItemKind,
        UiCommandKind? uiCommandKind,
        DateTimeOffset requestedAtUtc,
        ExecutionResourceSet resourceSet,
        string? description)
    {
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("ExecutionId cannot be empty.", nameof(executionId));
        }

        ExecutionId = executionId;
        SessionId = sessionId;
        OperationKind = operationKind;
        WorkItemKind = workItemKind;
        UiCommandKind = uiCommandKind;
        RequestedAtUtc = requestedAtUtc;
        ResourceSet = resourceSet ?? throw new ArgumentNullException(nameof(resourceSet));
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid ExecutionId { get; }

    public SessionId SessionId { get; }

    public ExecutionOperationKind OperationKind { get; }

    public SessionWorkItemKind? WorkItemKind { get; }

    public UiCommandKind? UiCommandKind { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public ExecutionResourceSet ResourceSet { get; }

    public string? Description { get; }
}
