using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SessionWorkItem(
    Guid WorkItemId,
    SessionId SessionId,
    SessionWorkItemKind Kind,
    DateTimeOffset EnqueuedAtUtc,
    string Reason,
    int Attempt)
{
    public static SessionWorkItem Create(
        SessionId sessionId,
        SessionWorkItemKind kind,
        DateTimeOffset enqueuedAtUtc,
        string reason) =>
        new(Guid.NewGuid(), sessionId, kind, enqueuedAtUtc, reason, 0);
}
