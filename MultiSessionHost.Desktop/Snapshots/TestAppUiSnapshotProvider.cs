using System.Net.Http.Json;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Snapshots;

public sealed class TestAppUiSnapshotProvider : IUiSnapshotProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TestAppUiSnapshotProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UiSnapshotEnvelope> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.TestAppHttpClientName);
        var snapshot = await client.GetFromJsonAsync<UiSnapshotEnvelope>(new Uri(attachment.BaseAddress, "ui-snapshot"), cancellationToken).ConfigureAwait(false);
        return snapshot ?? throw new InvalidOperationException($"The test app for session '{attachment.SessionId}' returned an empty UI snapshot.");
    }
}
