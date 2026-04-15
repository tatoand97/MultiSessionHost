using System.Net.Http.Json;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Adapters;

public class SelfHostedHttpDesktopTargetAdapter : IDesktopTargetAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUiSnapshotProvider _uiSnapshotProvider;

    public SelfHostedHttpDesktopTargetAdapter(
        IHttpClientFactory httpClientFactory,
        IUiSnapshotProvider uiSnapshotProvider)
    {
        _httpClientFactory = httpClientFactory;
        _uiSnapshotProvider = uiSnapshotProvider;
    }

    public virtual DesktopTargetKind Kind => DesktopTargetKind.SelfHostedHttpDesktop;

    public virtual Task AttachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task DetachAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment? attachment,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async Task ValidateAttachmentAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        if (context.Profile.SupportsStateEndpoint)
        {
            await SendGetAsync(attachment, DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.StatePath, "state"), cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual async Task ExecuteWorkItemAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        SessionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        switch (workItem.Kind)
        {
            case SessionWorkItemKind.Heartbeat when context.Profile.SupportsStateEndpoint:
                await SendGetAsync(attachment, DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.StatePath, "state"), cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.Tick:
                await SendPostAsync(attachment, DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.TickPath, "tick"), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public virtual Task<UiSnapshotEnvelope> CaptureUiSnapshotAsync(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        if (!context.Profile.SupportsUiSnapshots)
        {
            throw new InvalidOperationException($"Desktop target profile '{context.Profile.ProfileName}' does not support UI snapshots.");
        }

        return _uiSnapshotProvider.CaptureAsync(attachment, cancellationToken);
    }

    protected async Task<T> GetFromJsonAsync<T>(
        DesktopSessionAttachment attachment,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var baseAddress = attachment.BaseAddress
            ?? throw new InvalidOperationException($"The attached desktop target for session '{attachment.SessionId}' does not define BaseAddress.");
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.DesktopTargetHttpClientName);
        var payload = await client.GetFromJsonAsync<T>(new Uri(baseAddress, relativePath), cancellationToken).ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException($"The desktop target for session '{attachment.SessionId}' returned an empty payload for '{relativePath}'.");
    }

    protected async Task SendGetAsync(DesktopSessionAttachment attachment, string relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.DesktopTargetHttpClientName);
        using var response = await client.GetAsync(BuildUri(attachment, relativePath), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    protected async Task SendPostAsync(DesktopSessionAttachment attachment, string relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.DesktopTargetHttpClientName);
        using var response = await client.PostAsync(BuildUri(attachment, relativePath), content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static Uri BuildUri(DesktopSessionAttachment attachment, string relativePath)
    {
        var baseAddress = attachment.BaseAddress
            ?? throw new InvalidOperationException($"The attached desktop target for session '{attachment.SessionId}' does not define BaseAddress.");
        return new Uri(baseAddress, relativePath);
    }
}
