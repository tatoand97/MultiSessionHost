namespace MultiSessionHost.Desktop.Commands;

internal sealed class UiCommandFailureException : InvalidOperationException
{
    public UiCommandFailureException(string failureCode, string message)
        : base(message)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
