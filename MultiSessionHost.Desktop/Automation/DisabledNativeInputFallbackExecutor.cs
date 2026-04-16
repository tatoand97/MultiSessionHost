namespace MultiSessionHost.Desktop.Automation;

public sealed class DisabledNativeInputFallbackExecutor : INativeInputFallbackExecutor
{
    public Task<NativeInputFallbackResult> ClickAsync(
        INativeUiAutomationElement element,
        Models.ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        Task.FromResult(new NativeInputFallbackResult(false, "Native input fallback is disabled."));

    public Task<NativeInputFallbackResult> SetTextAsync(
        INativeUiAutomationElement element,
        Models.ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        Task.FromResult(new NativeInputFallbackResult(false, "Native keyboard fallback is disabled."));
}
