using System.Net;
using System.Net.Http.Json;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Adapters;

public sealed class TestDesktopAppUiInteractionAdapter : IUiInteractionAdapter
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TestDesktopAppUiInteractionAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public DesktopTargetKind Kind => DesktopTargetKind.DesktopTestApp;

    public Task<UiInteractionResult> ClickAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        PostAsync(
            attachment,
            BuildNodeUri(context, attachment, action.Node.Id.Value, DesktopTargetMetadata.UiClickNodePathTemplate, "ui/nodes/{nodeId}/click"),
            body: null,
            cancellationToken);

    public Task<UiInteractionResult> InvokeAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        PostAsync(
            attachment,
            BuildNodeUri(context, attachment, action.Node.Id.Value, DesktopTargetMetadata.UiInvokeNodePathTemplate, "ui/nodes/{nodeId}/invoke"),
            new UiInvokeRequest(action.ActionName, action.Metadata),
            cancellationToken);

    public Task<UiInteractionResult> SetTextAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        PostAsync(
            attachment,
            BuildNodeUri(context, attachment, action.Node.Id.Value, DesktopTargetMetadata.UiSetTextNodePathTemplate, "ui/nodes/{nodeId}/text"),
            new UiTextRequest(action.TextValue, action.Metadata),
            cancellationToken);

    public Task<UiInteractionResult> SelectItemAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        PostAsync(
            attachment,
            BuildNodeUri(context, attachment, action.Node.Id.Value, DesktopTargetMetadata.UiSelectNodePathTemplate, "ui/nodes/{nodeId}/select"),
            new UiSelectRequest(action.SelectedValue, action.Metadata),
            cancellationToken);

    public Task<UiInteractionResult> ToggleAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        PostAsync(
            attachment,
            BuildNodeUri(context, attachment, action.Node.Id.Value, DesktopTargetMetadata.UiToggleNodePathTemplate, "ui/nodes/{nodeId}/toggle"),
            new UiToggleRequest(action.BoolValue, action.Metadata),
            cancellationToken);

    private async Task<UiInteractionResult> PostAsync(
        DesktopSessionAttachment attachment,
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.DesktopTargetHttpClientName);
        using var response = body is null
            ? await client.PostAsync(uri, content: null, cancellationToken).ConfigureAwait(false)
            : await client.PostAsJsonAsync(uri, body, cancellationToken).ConfigureAwait(false);

        UiInteractionResult? payload = null;

        try
        {
            payload = await response.Content.ReadFromJsonAsync<UiInteractionResult>(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Fall back to HTTP status details below when the cooperative app does not return JSON.
        }

        if (response.IsSuccessStatusCode)
        {
            return payload ?? UiInteractionResult.Success("The cooperative UI command completed successfully.", DateTimeOffset.UtcNow);
        }

        if (payload is not null)
        {
            return payload;
        }

        var failureCode = response.StatusCode switch
        {
            HttpStatusCode.NotFound => UiCommandFailureCodes.NodeNotFound,
            HttpStatusCode.BadRequest => UiCommandFailureCodes.InvalidCommandPayload,
            HttpStatusCode.Conflict => UiCommandFailureCodes.UnsupportedCommand,
            _ => UiCommandFailureCodes.InteractionFailed
        };

        var reasonPhrase = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
        return UiInteractionResult.Failure(
            $"The cooperative UI endpoint '{uri}' failed with HTTP {(int)response.StatusCode} ({reasonPhrase}).",
            failureCode,
            DateTimeOffset.UtcNow);
    }

    private static Uri BuildNodeUri(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        string nodeId,
        string metadataKey,
        string defaultTemplate)
    {
        var template = DesktopTargetMetadata.GetValue(context.Target.Metadata, metadataKey, defaultTemplate);
        var relativePath = template.Replace("{nodeId}", Uri.EscapeDataString(nodeId), StringComparison.Ordinal);
        var baseAddress = attachment.BaseAddress
            ?? throw new InvalidOperationException($"The attached desktop target for session '{attachment.SessionId}' does not define BaseAddress.");
        return new Uri(baseAddress, relativePath);
    }
}
