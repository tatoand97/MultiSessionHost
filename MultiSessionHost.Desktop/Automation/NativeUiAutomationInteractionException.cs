namespace MultiSessionHost.Desktop.Automation;

public sealed class NativeUiAutomationInteractionException : InvalidOperationException
{
    public NativeUiAutomationInteractionException(string failureCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
