using MultiSessionHost.Core.Enums;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Core.Models;

public sealed record UiCommandResult(
    bool Succeeded,
    SessionId SessionId,
    UiNodeId? NodeId,
    UiCommandKind Kind,
    string Message,
    DateTimeOffset ExecutedAtUtc,
    bool UpdatedUiStateAvailable,
    string? FailureCode)
{
    public static UiCommandResult Success(
        SessionId sessionId,
        UiNodeId? nodeId,
        UiCommandKind kind,
        string message,
        DateTimeOffset executedAtUtc,
        bool updatedUiStateAvailable) =>
        new(
            Succeeded: true,
            sessionId,
            nodeId,
            kind,
            message,
            executedAtUtc,
            updatedUiStateAvailable,
            FailureCode: null);

    public static UiCommandResult Failure(
        SessionId sessionId,
        UiNodeId? nodeId,
        UiCommandKind kind,
        string message,
        DateTimeOffset executedAtUtc,
        string failureCode,
        bool updatedUiStateAvailable = false) =>
        new(
            Succeeded: false,
            sessionId,
            nodeId,
            kind,
            message,
            executedAtUtc,
            updatedUiStateAvailable,
            failureCode);
}
