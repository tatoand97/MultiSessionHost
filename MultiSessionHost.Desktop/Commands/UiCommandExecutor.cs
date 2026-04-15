using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Commands;

public sealed class UiCommandExecutor : IUiCommandExecutor
{
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly ISessionAttachmentOperations _attachmentOperations;
    private readonly IExecutionCoordinator _executionCoordinator;
    private readonly IExecutionResourceResolver _executionResourceResolver;
    private readonly ISessionUiRefreshService _uiRefreshService;
    private readonly IUiActionResolver _actionResolver;
    private readonly IReadOnlyDictionary<DesktopTargetKind, IUiInteractionAdapter> _interactionAdapters;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly IClock _clock;
    private readonly ILogger<UiCommandExecutor> _logger;

    public UiCommandExecutor(
        ISessionCoordinator sessionCoordinator,
        IDesktopTargetProfileResolver targetProfileResolver,
        ISessionAttachmentOperations attachmentOperations,
        IExecutionCoordinator executionCoordinator,
        IExecutionResourceResolver executionResourceResolver,
        ISessionUiRefreshService uiRefreshService,
        IUiActionResolver actionResolver,
        IEnumerable<IUiInteractionAdapter> interactionAdapters,
        IObservabilityRecorder observabilityRecorder,
        IClock clock,
        ILogger<UiCommandExecutor> logger)
    {
        _sessionCoordinator = sessionCoordinator;
        _targetProfileResolver = targetProfileResolver;
        _attachmentOperations = attachmentOperations;
        _executionCoordinator = executionCoordinator;
        _executionResourceResolver = executionResourceResolver;
        _uiRefreshService = uiRefreshService;
        _actionResolver = actionResolver;
        _observabilityRecorder = observabilityRecorder;
        _clock = clock;
        _logger = logger;
        _interactionAdapters = interactionAdapters.ToDictionary(static adapter => adapter.Kind);
    }

    public async Task<UiCommandResult> ExecuteAsync(UiCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var startedAt = _clock.UtcNow;

        try
        {
            var session = _sessionCoordinator.GetSession(command.SessionId);

            if (session is null)
            {
                return Fail(command, $"Session '{command.SessionId}' was not found.", UiCommandFailureCodes.SessionNotFound);
            }

            if (session.Runtime.CurrentStatus is not (SessionStatus.Starting or SessionStatus.Running or SessionStatus.Paused))
            {
                return Fail(
                    command,
                    $"Session '{command.SessionId}' must be active before executing UI commands.",
                    UiCommandFailureCodes.SessionNotActive);
            }

            var context = _targetProfileResolver.Resolve(session);
            var request = _executionResourceResolver.CreateForUiCommand(session, context, command);

            await using var lease = await _executionCoordinator.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
            var attachment = await _attachmentOperations.EnsureAttachedAsync(session, context, cancellationToken).ConfigureAwait(false);

            if (command.Kind == UiCommandKind.RefreshUi)
            {
                var refreshResult = await RefreshUiAsync(command, session, context, attachment, cancellationToken).ConfigureAwait(false);
                await _observabilityRecorder.RecordCommandExecutionAsync(command, refreshResult, _clock.UtcNow - startedAt, context.Profile.ProfileName, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
                return refreshResult;
            }

            var uiState = _sessionCoordinator.GetSessionUiState(command.SessionId);

            if (uiState?.ProjectedTree is null)
            {
                try
                {
                    uiState = await _uiRefreshService.RefreshAsync(session, context, attachment, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    var failure = Fail(command, exception.Message, UiCommandFailureCodes.UiRefreshFailed);
                    await _observabilityRecorder.RecordCommandExecutionAsync(command, failure, _clock.UtcNow - startedAt, context.Profile.ProfileName, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
                    return failure;
                }
            }

            if (uiState.ProjectedTree is null)
            {
                return Fail(
                    command,
                    $"Session '{command.SessionId}' does not have projected UI state available.",
                    UiCommandFailureCodes.UiStateUnavailable);
            }

            var resolvedAction = _actionResolver.Resolve(uiState.ProjectedTree, command);
            var interactionAdapter = ResolveInteractionAdapter(context.Profile.Kind);
            var interactionResult = await ExecuteResolvedActionAsync(interactionAdapter, context, attachment, resolvedAction, cancellationToken).ConfigureAwait(false);

            if (!interactionResult.Succeeded)
            {
                var failure = UiCommandResult.Failure(
                    command.SessionId,
                    command.NodeId,
                    command.Kind,
                    interactionResult.Message,
                    interactionResult.ExecutedAtUtc,
                    interactionResult.FailureCode ?? UiCommandFailureCodes.InteractionFailed);

                await _observabilityRecorder.RecordCommandExecutionAsync(command, failure, _clock.UtcNow - startedAt, context.Profile.ProfileName, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
                return failure;
            }

            var success = await RefreshUiAfterSuccessAsync(command, session, context, attachment, interactionResult, cancellationToken).ConfigureAwait(false);
            await _observabilityRecorder.RecordCommandExecutionAsync(command, success, _clock.UtcNow - startedAt, context.Profile.ProfileName, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            return success;
        }
        catch (UiCommandFailureException exception)
        {
            var failure = Fail(command, exception.Message, exception.FailureCode);
            await _observabilityRecorder.RecordCommandExecutionAsync(command, failure, _clock.UtcNow - startedAt, null, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            return failure;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "UI command '{Kind}' failed for session '{SessionId}'.", command.Kind, command.SessionId);
            var failure = Fail(command, exception.Message, UiCommandFailureCodes.InteractionFailed);
            await _observabilityRecorder.RecordCommandExecutionAsync(command, failure, _clock.UtcNow - startedAt, null, nameof(UiCommandExecutor), new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            return failure;
        }
    }

    private async Task<UiCommandResult> RefreshUiAsync(
        UiCommand command,
        SessionSnapshot session,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _uiRefreshService.RefreshAsync(session, context, attachment, cancellationToken).ConfigureAwait(false);

            return UiCommandResult.Success(
                command.SessionId,
                command.NodeId,
                command.Kind,
                $"UI refresh completed for session '{command.SessionId}'.",
                _clock.UtcNow,
                updatedUiStateAvailable: state.ProjectedTree is not null);
        }
        catch (Exception exception)
        {
            return Fail(command, exception.Message, UiCommandFailureCodes.UiRefreshFailed);
        }
    }

    private async Task<UiCommandResult> RefreshUiAfterSuccessAsync(
        UiCommand command,
        SessionSnapshot session,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        UiInteractionResult interactionResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _uiRefreshService.RefreshAsync(session, context, attachment, cancellationToken).ConfigureAwait(false);
            return UiCommandResult.Success(
                command.SessionId,
                command.NodeId,
                command.Kind,
                interactionResult.Message,
                interactionResult.ExecutedAtUtc,
                updatedUiStateAvailable: state.ProjectedTree is not null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "UI command '{Kind}' executed for session '{SessionId}', but the post-action refresh failed.",
                command.Kind,
                command.SessionId);

            return UiCommandResult.Success(
                command.SessionId,
                command.NodeId,
                command.Kind,
                $"{interactionResult.Message} UI refresh failed after the command: {exception.Message}",
                interactionResult.ExecutedAtUtc,
                updatedUiStateAvailable: false);
        }
    }

    private async Task<UiInteractionResult> ExecuteResolvedActionAsync(
        IUiInteractionAdapter interactionAdapter,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        action.Kind switch
        {
            UiCommandKind.ClickNode => await interactionAdapter.ClickAsync(context, attachment, action, cancellationToken).ConfigureAwait(false),
            UiCommandKind.InvokeNodeAction => await interactionAdapter.InvokeAsync(context, attachment, action, cancellationToken).ConfigureAwait(false),
            UiCommandKind.SetText => await interactionAdapter.SetTextAsync(context, attachment, action, cancellationToken).ConfigureAwait(false),
            UiCommandKind.SelectItem => await interactionAdapter.SelectItemAsync(context, attachment, action, cancellationToken).ConfigureAwait(false),
            UiCommandKind.ToggleNode => await interactionAdapter.ToggleAsync(context, attachment, action, cancellationToken).ConfigureAwait(false),
            _ => throw new UiCommandFailureException(
                UiCommandFailureCodes.UnsupportedCommand,
                $"UiCommand '{action.Kind}' is not supported by the interaction adapter.")
        };

    private IUiInteractionAdapter ResolveInteractionAdapter(DesktopTargetKind kind) =>
        _interactionAdapters.TryGetValue(kind, out var adapter)
            ? adapter
            : throw new UiCommandFailureException(
                UiCommandFailureCodes.InteractionAdapterNotRegistered,
                $"No UI interaction adapter is registered for desktop target kind '{kind}'.");

    private UiCommandResult Fail(UiCommand command, string message, string failureCode) =>
        UiCommandResult.Failure(
            command.SessionId,
            command.NodeId,
            command.Kind,
            message,
            _clock.UtcNow,
            failureCode);
}
