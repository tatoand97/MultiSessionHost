using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.DependencyInjection;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Drivers;

public sealed class DesktopTestAppSessionDriver : ISessionDriver
{
    private readonly SessionHostOptions _options;
    private readonly ISessionAttachmentResolver _attachmentResolver;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IUiSnapshotProvider _uiSnapshotProvider;
    private readonly IUiSnapshotSerializer _uiSnapshotSerializer;
    private readonly IUiTreeNormalizer _uiTreeNormalizer;
    private readonly IUiStateProjector _uiStateProjector;
    private readonly IWorkItemPlanner _workItemPlanner;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly IClock _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DesktopTestAppSessionDriver> _logger;

    public DesktopTestAppSessionDriver(
        SessionHostOptions options,
        ISessionAttachmentResolver attachmentResolver,
        IAttachedSessionStore attachedSessionStore,
        IUiSnapshotProvider uiSnapshotProvider,
        IUiSnapshotSerializer uiSnapshotSerializer,
        IUiTreeNormalizer uiTreeNormalizer,
        IUiStateProjector uiStateProjector,
        IWorkItemPlanner workItemPlanner,
        ISessionUiStateStore sessionUiStateStore,
        IClock clock,
        IHttpClientFactory httpClientFactory,
        ILogger<DesktopTestAppSessionDriver> logger)
    {
        _options = options;
        _attachmentResolver = attachmentResolver;
        _attachedSessionStore = attachedSessionStore;
        _uiSnapshotProvider = uiSnapshotProvider;
        _uiSnapshotSerializer = uiSnapshotSerializer;
        _uiTreeNormalizer = uiTreeNormalizer;
        _uiStateProjector = uiStateProjector;
        _workItemPlanner = workItemPlanner;
        _sessionUiStateStore = sessionUiStateStore;
        _clock = clock;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task AttachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var attachment = await _attachmentResolver.ResolveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var state = await GetStateAsync(attachment, cancellationToken).ConfigureAwait(false);
        ValidateState(snapshot.SessionId, attachment, state);
        await _attachedSessionStore.SetAsync(attachment, cancellationToken).ConfigureAwait(false);
    }

    public Task DetachAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) =>
        _attachedSessionStore.RemoveAsync(snapshot.SessionId, cancellationToken).AsTask();

    public async Task ExecuteWorkItemAsync(SessionSnapshot snapshot, SessionWorkItem workItem, CancellationToken cancellationToken)
    {
        var attachment = await EnsureAttachmentAsync(snapshot, cancellationToken).ConfigureAwait(false);

        switch (workItem.Kind)
        {
            case SessionWorkItemKind.Heartbeat:
                await GetStateAsync(attachment, cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.Tick:
                await PostAsync(attachment, "tick", cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.FetchUiSnapshot:
                EnsureUiSnapshotsEnabled();
                await FetchUiSnapshotAsync(snapshot.SessionId, attachment, cancellationToken).ConfigureAwait(false);
                break;

            case SessionWorkItemKind.ProjectUiState:
                EnsureUiSnapshotsEnabled();
                await ProjectUiStateAsync(snapshot.SessionId, attachment, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task<DesktopSessionAttachment> EnsureAttachmentAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var current = await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false);

        if (current is not null)
        {
            return current;
        }

        await AttachAsync(snapshot, cancellationToken).ConfigureAwait(false);

        return await _attachedSessionStore.GetAsync(snapshot.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{snapshot.SessionId}' could not be attached.");
    }

    private async Task FetchUiSnapshotAsync(SessionId sessionId, DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _uiSnapshotProvider.CaptureAsync(attachment, cancellationToken).ConfigureAwait(false);
            var rawJson = _uiSnapshotSerializer.Serialize(snapshot);

            await _sessionUiStateStore.UpdateAsync(
                sessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = snapshot.CapturedAtUtc,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(sessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ProjectUiStateAsync(SessionId sessionId, DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        try
        {
            var uiState = await _sessionUiStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"UI state for session '{sessionId}' was not initialized.");

            UiSnapshotEnvelope snapshot;
            string rawJson;

            if (string.IsNullOrWhiteSpace(uiState.RawSnapshotJson))
            {
                snapshot = await _uiSnapshotProvider.CaptureAsync(attachment, cancellationToken).ConfigureAwait(false);
                rawJson = _uiSnapshotSerializer.Serialize(snapshot);

                uiState = await _sessionUiStateStore.UpdateAsync(
                    sessionId,
                    current => current with
                    {
                        RawSnapshotJson = rawJson,
                        LastSnapshotCapturedAtUtc = snapshot.CapturedAtUtc
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                rawJson = uiState.RawSnapshotJson;
                snapshot = _uiSnapshotSerializer.Deserialize(rawJson);
            }

            var metadata = new UiSnapshotMetadata(
                sessionId.Value,
                Source: "DesktopTestApp",
                snapshot.CapturedAtUtc,
                snapshot.Process.ProcessId,
                snapshot.Window.WindowHandle,
                snapshot.Window.Title,
                snapshot.Metadata);

            var tree = _uiTreeNormalizer.Normalize(metadata, snapshot.Root);
            var diff = _uiStateProjector.Project(uiState.ProjectedTree, tree);
            var plannedWorkItems = _workItemPlanner.Plan(tree);

            await _sessionUiStateStore.UpdateAsync(
                sessionId,
                current => current with
                {
                    RawSnapshotJson = rawJson,
                    LastSnapshotCapturedAtUtc = snapshot.CapturedAtUtc,
                    ProjectedTree = tree,
                    LastDiff = diff,
                    PlannedWorkItems = plannedWorkItems,
                    LastRefreshCompletedAtUtc = _clock.UtcNow,
                    LastRefreshError = null,
                    LastRefreshErrorAtUtc = null
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RecordUiRefreshErrorAsync(sessionId, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecordUiRefreshErrorAsync(SessionId sessionId, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "UI refresh failed for session '{SessionId}'.", sessionId);

        await _sessionUiStateStore.UpdateAsync(
            sessionId,
            current => current with
            {
                LastRefreshError = exception.Message,
                LastRefreshErrorAtUtc = _clock.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TestDesktopAppState> GetStateAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.TestAppHttpClientName);
        var state = await client.GetFromJsonAsync<TestDesktopAppState>(new Uri(attachment.BaseAddress, "state"), cancellationToken).ConfigureAwait(false);
        return state ?? throw new InvalidOperationException($"The test desktop app for session '{attachment.SessionId}' returned an empty state payload.");
    }

    private async Task PostAsync(DesktopSessionAttachment attachment, string relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DesktopServiceCollectionExtensions.TestAppHttpClientName);
        using var response = await client.PostAsync(new Uri(attachment.BaseAddress, relativePath), content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
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

        if (state.Port != attachment.BaseAddress.Port)
        {
            throw new InvalidOperationException($"The attached desktop app for session '{sessionId}' reported Port '{state.Port}' instead of '{attachment.BaseAddress.Port}'.");
        }
    }

    private void EnsureUiSnapshotsEnabled()
    {
        if (!_options.EnableUiSnapshots)
        {
            throw new InvalidOperationException("UI snapshots are disabled. Set EnableUiSnapshots=true to request raw or projected UI state.");
        }
    }
}
