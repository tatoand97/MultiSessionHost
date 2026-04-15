using System.Net.Http.Json;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class SelfHostedHttpUiSnapshotProvider : IUiSnapshotProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SelfHostedHttpUiSnapshotProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UiSnapshotEnvelope> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        var baseAddress = attachment.BaseAddress
            ?? throw new InvalidOperationException($"The attached desktop target for session '{attachment.SessionId}' does not define BaseAddress.");
        var relativePath = DesktopTargetMetadata.GetValue(attachment.Target.Metadata, DesktopTargetMetadata.UiSnapshotPath, "ui-snapshot");
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.DesktopTargetHttpClientName);
        var snapshot = await client.GetFromJsonAsync<UiSnapshotEnvelope>(new Uri(baseAddress, relativePath), cancellationToken).ConfigureAwait(false);
        return snapshot ?? throw new InvalidOperationException($"The desktop target for session '{attachment.SessionId}' returned an empty UI snapshot.");
    }
}
