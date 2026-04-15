using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Commands;

public sealed class UiCommandExecutor : IUiCommandExecutor
{
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IDesktopTargetProfileResolver _targetProfileResolver;
    private readonly IDesktopTargetAdapterRegistry _targetAdapterRegistry;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IUiActionResolver _actionResolver;
    private readonly IReadOnlyDictionary<DesktopTargetKind, IUiInteractionAdapter> _interactionAdapters;
    private readonly IClock _clock;
    private readonly ILogger<UiCommandExecutor> _logger;

    public UiCommandExecutor(
        ISessionCoordinator sessionCoordinator,
        IDesktopTargetProfileResolver targetProfileResolver,
        IDesktopTargetAdapterRegistry targetAdapterRegistry,
        IAttachedSessionStore attachedSessionStore,
        IUiActionResolver actionResolver,
        IEnumerable<IUiInteractionAdapter> interactionAdapters,
        IClock clock,
        ILogger<UiCommandExecutor> logger)
    {
        _sessionCoordinator = sessionCoordinator;
        _targetProfileResolver = targetProfileResolver;
        _targetAdapterRegistry = targetAdapterRegistry;
        _attachedSessionStore = attachedSessionStore;
        _actionResolver = actionResolver;
        _clock = clock;
        _logger = logger;
        _interactionAdapters = interactionAdapters.ToDictionary(static adapter => adapter.Kind);
    }

    public async Task<UiCommandResult> ExecuteAsync(UiCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var session = _sessionCoordinator.GetSession(command.SessionId);

            if (session is null)
            {
                return Fail(command, $"Session '{command.SessionId}' was not found.", UiCommandFailureCodes.SessionNotFound);
            }

            if (command.Kind == UiCommandKind.RefreshUi)
            {
                return await RefreshUiAsync(command, cancellationToken).ConfigureAwait(false);
            }

            if (session.Runtime.CurrentStatus is not (SessionStatus.Starting or SessionStatus.Running or SessionStatus.Paused))
            {
                return Fail(
                    command,
                    $"Session '{command.SessionId}' must be active before executing UI commands.",
                    UiCommandFailureCodes.SessionNotActive);
            }

            var uiState = _sessionCoordinator.GetSessionUiState(command.SessionId);

            if (uiState?.ProjectedTree is null)
            {
                uiState = await _sessionCoordinator.RefreshSessionUiAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
            }

            if (uiState.ProjectedTree is null)
            {
                return Fail(
                    command,
                    $"Session '{command.SessionId}' does not have projected UI state available.",
                    UiCommandFailureCodes.UiStateUnavailable);
            }

            var context = _targetProfileResolver.Resolve(session);
            var targetAdapter = _targetAdapterRegistry.Resolve(context.Profile.Kind);
            var attachment = await _attachedSessionStore.GetAsync(command.SessionId, cancellationToken).ConfigureAwait(false);

            if (attachment is null)
            {
                return Fail(
                    command,
                    $"Session '{command.SessionId}' is active but has no current target attachment.",
                    UiCommandFailureCodes.TargetNotAttached);
            }

            await targetAdapter.ValidateAttachmentAsync(session, context, attachment, cancellationToken).ConfigureAwait(false);

            var resolvedAction = _actionResolver.Resolve(uiState.ProjectedTree, command);
            var interactionAdapter = ResolveInteractionAdapter(context.Profile.Kind);
            var interactionResult = await ExecuteResolvedActionAsync(interactionAdapter, context, attachment, resolvedAction, cancellationToken).ConfigureAwait(false);

            if (!interactionResult.Succeeded)
            {
                return UiCommandResult.Failure(
                    command.SessionId,
                    command.NodeId,
                    command.Kind,
                    interactionResult.Message,
                    interactionResult.ExecutedAtUtc,
                    interactionResult.FailureCode ?? UiCommandFailureCodes.InteractionFailed);
            }

            return await RefreshUiAfterSuccessAsync(command, interactionResult, cancellationToken).ConfigureAwait(false);
        }
        catch (UiCommandFailureException exception)
        {
            return Fail(command, exception.Message, exception.FailureCode);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "UI command '{Kind}' failed for session '{SessionId}'.", command.Kind, command.SessionId);
            return Fail(command, exception.Message, UiCommandFailureCodes.InteractionFailed);
        }
    }

    private async Task<UiCommandResult> RefreshUiAsync(UiCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _sessionCoordinator.RefreshSessionUiAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
            var message = state.LastRefreshError is null
                ? $"UI refresh completed for session '{command.SessionId}'."
                : $"UI refresh completed with error for session '{command.SessionId}': {state.LastRefreshError}";

            if (state.LastRefreshError is not null)
            {
                return UiCommandResult.Failure(
                    command.SessionId,
                    command.NodeId,
                    command.Kind,
                    message,
                    _clock.UtcNow,
                    UiCommandFailureCodes.UiRefreshFailed,
                    updatedUiStateAvailable: state.ProjectedTree is not null);
            }

            return UiCommandResult.Success(
                command.SessionId,
                command.NodeId,
                command.Kind,
                message,
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
        UiInteractionResult interactionResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _sessionCoordinator.RefreshSessionUiAsync(command.SessionId, cancellationToken).ConfigureAwait(false);
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
