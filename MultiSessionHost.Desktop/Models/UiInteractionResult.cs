namespace MultiSessionHost.Desktop.Models;

public sealed record UiInteractionResult(
    bool Succeeded,
    string Message,
    string? FailureCode,
    DateTimeOffset ExecutedAtUtc)
{
    public static UiInteractionResult Success(string message, DateTimeOffset executedAtUtc) =>
        new(true, message, FailureCode: null, executedAtUtc);

    public static UiInteractionResult Failure(string message, string failureCode, DateTimeOffset executedAtUtc) =>
        new(false, message, failureCode, executedAtUtc);
}
