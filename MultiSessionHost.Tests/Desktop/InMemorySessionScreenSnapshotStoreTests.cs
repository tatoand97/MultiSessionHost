using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Tests.Desktop;

public sealed class InMemorySessionScreenSnapshotStoreTests
{
    [Fact]
    public async Task UpsertLatestAsync_StoresAndRetrievesLatestSnapshotPerSession()
    {
        var store = CreateStore(maxHistoryEntries: 2);
        var sessionId = new SessionId("alpha");
        var first = CreateSnapshot(sessionId, sequence: 1, capturedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1), payload: [1, 2, 3]);
        var second = CreateSnapshot(sessionId, sequence: 2, capturedAtUtc: DateTimeOffset.UtcNow, payload: [4, 5, 6, 7]);

        await store.UpsertLatestAsync(sessionId, first, CancellationToken.None);
        await store.UpsertLatestAsync(sessionId, second, CancellationToken.None);

        var latest = await store.GetLatestAsync(sessionId, CancellationToken.None);
        var history = await store.GetHistoryAsync(sessionId, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(2, latest!.Sequence);
        Assert.Equal(4, latest.PayloadByteLength);
        Assert.Equal(2, history.Entries.Count);
        Assert.Equal([1L, 2L], history.Entries.Select(static entry => entry.Sequence).ToArray());
    }

    [Fact]
    public async Task GetAllLatestSummariesAsync_ReturnsLightweightMetadataWithoutPayload()
    {
        var store = CreateStore();
        var sessionId = new SessionId("alpha");
        var snapshot = CreateSnapshot(sessionId, sequence: 1, capturedAtUtc: DateTimeOffset.UtcNow, payload: Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray());

        await store.UpsertLatestAsync(sessionId, snapshot, CancellationToken.None);

        var summaries = await store.GetAllLatestSummariesAsync(CancellationToken.None);
        var summary = Assert.Single(summaries);

        Assert.Equal(sessionId, summary.SessionId);
        Assert.Equal(64, summary.PayloadByteLength);
        Assert.Equal("ScreenCapture", summary.CaptureSource);
        Assert.Equal("ScreenCaptureDesktop", summary.TargetKind.ToString());
        Assert.Null(typeof(SessionScreenSnapshotSummary).GetProperty("ImageBytes"));
    }

    private static InMemorySessionScreenSnapshotStore CreateStore(int maxHistoryEntries = 10) =>
        new(new SessionHostOptions
        {
            ScreenSnapshots = new ScreenSnapshotStoreOptions
            {
                MaxHistoryEntriesPerSession = maxHistoryEntries
            }
        });

    private static SessionScreenSnapshot CreateSnapshot(
        SessionId sessionId,
        long sequence,
        DateTimeOffset capturedAtUtc,
        byte[] payload) =>
        new(
            sessionId,
            sequence,
            capturedAtUtc,
            ProcessId: 321,
            ProcessName: "ScreenApp",
            WindowHandle: 999,
            WindowTitle: "Screen Fixture",
            WindowBounds: new UiBounds(10, 20, 800, 600),
            ImageWidth: 800,
            ImageHeight: 600,
            ImageFormat: "image/png",
            PixelFormat: "Format32bppArgb",
            ImageBytes: payload,
            PayloadByteLength: payload.Length,
            TargetKind: DesktopTargetKind.ScreenCaptureDesktop,
            CaptureSource: "ScreenCapture",
            ObservabilityBackend: "ScreenCapture",
            CaptureBackend: "FakeCapture",
            CaptureDurationMs: 12.5d,
            CaptureOrigin: "LiveRefresh",
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["captureBackend"] = "FakeCapture"
            });
}
