using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Adapters;

public sealed class DesktopTestAppTargetAdapter : SelfHostedHttpDesktopTargetAdapter
{
    public DesktopTestAppTargetAdapter(
        IHttpClientFactory httpClientFactory,
        IUiSnapshotProvider uiSnapshotProvider)
        : base(httpClientFactory, uiSnapshotProvider)
    {
    }

    public override DesktopTargetKind Kind => DesktopTargetKind.DesktopTestApp;

    public override async Task ValidateAttachmentAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        var state = await GetFromJsonAsync<TestDesktopAppState>(
            attachment,
            DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.StatePath, "state"),
            cancellationToken).ConfigureAwait(false);

        ValidateState(snapshot.SessionId, attachment, state);
    }

    private static void ValidateState(SessionId sessionId, DesktopSessionAttachment attachment, TestDesktopAppState state)
    {
        if (!string.Equals(state.SessionId, sessionId.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The attached desktop app reported SessionId '{state.SessionId}' instead of '{sessionId}'.");
        }

        if (state.ProcessId != attachment.Process.ProcessId)
        {
            throw new InvalidOperationException($"The attached desktop app for session '{sessionId}' reported ProcessId '{state.ProcessId}' instead of '{attachment.Process.ProcessId}'.");
        }

        if (state.WindowHandle != attachment.Window.WindowHandle)
        {
            throw new InvalidOperationException($"The attached desktop app for session '{sessionId}' reported WindowHandle '{state.WindowHandle}' instead of '{attachment.Window.WindowHandle}'.");
        }

        if (attachment.BaseAddress is null)
        {
            throw new InvalidOperationException($"The attached desktop app for session '{sessionId}' does not define BaseAddress.");
        }

        if (state.Port != attachment.BaseAddress.Port)
        {
            throw new InvalidOperationException($"The attached desktop app for session '{sessionId}' reported Port '{state.Port}' instead of '{attachment.BaseAddress.Port}'.");
        }
    }
}
