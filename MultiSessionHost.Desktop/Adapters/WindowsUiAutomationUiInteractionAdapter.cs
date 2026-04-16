using System.Windows.Automation;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;

namespace MultiSessionHost.Desktop.Adapters;

public sealed class WindowsUiAutomationUiInteractionAdapter : IUiInteractionAdapter
{
    private readonly INativeUiAutomationElementLocator _locator;
    private readonly INativeInputFallbackExecutor _fallbackExecutor;
    private readonly IAttachedSessionStore _attachedSessionStore;
    private readonly IProcessLocator _processLocator;
    private readonly IWindowLocator _windowLocator;
    private readonly ISessionRecoveryStateStore _recoveryStateStore;
    private readonly IObservabilityRecorder _observabilityRecorder;
    private readonly SessionHostOptions _options;
    private readonly IClock _clock;

    public WindowsUiAutomationUiInteractionAdapter(
        INativeUiAutomationElementLocator locator,
        INativeInputFallbackExecutor fallbackExecutor,
        IAttachedSessionStore attachedSessionStore,
        IProcessLocator processLocator,
        IWindowLocator windowLocator,
        ISessionRecoveryStateStore recoveryStateStore,
        IObservabilityRecorder observabilityRecorder,
        SessionHostOptions options,
        IClock clock)
    {
        _locator = locator;
        _fallbackExecutor = fallbackExecutor;
        _attachedSessionStore = attachedSessionStore;
        _processLocator = processLocator;
        _windowLocator = windowLocator;
        _recoveryStateStore = recoveryStateStore;
        _observabilityRecorder = observabilityRecorder;
        _options = options;
        _clock = clock;
    }

    public DesktopTargetKind Kind => DesktopTargetKind.WindowsUiAutomationDesktop;

    public Task<UiInteractionResult> ClickAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("click", context, attachment, action, ClickCoreAsync, cancellationToken);

    public Task<UiInteractionResult> InvokeAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("invoke", context, attachment, action, InvokeCoreAsync, cancellationToken);

    public Task<UiInteractionResult> SetTextAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("setText", context, attachment, action, SetTextCoreAsync, cancellationToken);

    public Task<UiInteractionResult> SelectItemAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("select", context, attachment, action, SelectCoreAsync, cancellationToken);

    public Task<UiInteractionResult> ToggleAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken) =>
        ExecuteAsync("toggle", context, attachment, action, ToggleCoreAsync, cancellationToken);

    private async Task<UiInteractionResult> ExecuteAsync(
        string operation,
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        Func<ResolvedDesktopTargetContext, DesktopSessionAttachment, ResolvedUiAction, LocatedNativeUiElement, CancellationToken, Task<NativeActionOutcome>> execute,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        var metadata = Metadata(context, attachment, action, operation);
        await RecordActivityAsync(attachment, "native.action.started", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, metadata, cancellationToken).ConfigureAwait(false);

        try
        {
            await ValidateAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
            var located = await LocateWithRetryAsync(context, attachment, action, cancellationToken).ConfigureAwait(false);

            if (_options.NativeInteraction.PreActionFocusEnabled)
            {
                await RecordActivityAsync(attachment, "native.action.focus.started", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, metadata, cancellationToken).ConfigureAwait(false);
                if (located.Element.TrySetFocus())
                {
                    await RecordActivityAsync(attachment, "native.action.focus.succeeded", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, metadata, cancellationToken).ConfigureAwait(false);
                }
            }

            var outcome = await ExecuteWithRetryAsync(context, attachment, action, located, execute, cancellationToken).ConfigureAwait(false);
            var duration = _clock.UtcNow - startedAt;
            RuntimeObservability.NativeActionTotal.Add(1, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
            RuntimeObservability.NativeActionDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
            await _recoveryStateStore.RegisterSuccessAsync(attachment.SessionId, $"native.action.{operation}", "recovery.success_cleared_failures", outcome.Message, metadata, cancellationToken).ConfigureAwait(false);
            await RecordActivityAsync(attachment, "native.action.succeeded", SessionObservabilityOutcome.Success, duration, null, outcome.Message, metadata, cancellationToken).ConfigureAwait(false);

            return UiInteractionResult.Success(outcome.Message, _clock.UtcNow);
        }
        catch (NativeUiAutomationInteractionException exception)
        {
            return await FailAsync(attachment, operation, exception.FailureCode, exception.Message, startedAt, exception, metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (ElementNotAvailableException exception)
        {
            return await FailAsync(attachment, operation, UiCommandFailureCodes.NativeTargetLostDuringAction, exception.Message, startedAt, exception, metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await FailAsync(attachment, operation, UiCommandFailureCodes.NativePatternFailed, exception.Message, startedAt, exception, metadata, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<LocatedNativeUiElement> LocateWithRetryAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.UtcNow;
        await RecordActivityAsync(attachment, "native.action.locate.started", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, Metadata(context, attachment, action, "locate"), cancellationToken).ConfigureAwait(false);

        try
        {
            var located = await RetryAsync(() => _locator.LocateAsync(context, attachment, action, cancellationToken), cancellationToken).ConfigureAwait(false);
            var duration = _clock.UtcNow - startedAt;
            RuntimeObservability.NativeActionLocateDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
            await RecordActivityAsync(attachment, "native.action.locate.succeeded", SessionObservabilityOutcome.Success, duration, null, located.MatchStrategy, Metadata(context, attachment, action, "locate"), cancellationToken).ConfigureAwait(false);
            return located;
        }
        catch (Exception exception)
        {
            var duration = _clock.UtcNow - startedAt;
            var code = exception is NativeUiAutomationInteractionException native ? native.FailureCode : UiCommandFailureCodes.NativeElementNotFound;
            await RecordActivityAsync(attachment, "native.action.locate.failed", SessionObservabilityOutcome.Failure, duration, code, exception.Message, Metadata(context, attachment, action, "locate"), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private Task<NativeActionOutcome> ClickCoreAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        CancellationToken cancellationToken)
    {
        if (IsToggleLike(action) && located.Element.TryToggle())
        {
            return VerifiedAsync(attachment, "native.action.pattern.toggle", "Clicked native toggle via TogglePattern.", action, located, VerifyToggle, cancellationToken);
        }

        if (IsSelectionItem(action) && located.Element.TrySelect())
        {
            return VerifiedAsync(attachment, "native.action.pattern.select", "Clicked native selectable item via SelectionItemPattern.", action, located, VerifySelected, cancellationToken);
        }

        if (IsExpandAction(action) && located.Element.TryExpand())
        {
            return PatternAsync(attachment, "native.action.pattern.expand", "Expanded native element via ExpandCollapsePattern.", action, cancellationToken);
        }

        if (IsCollapseAction(action) && located.Element.TryCollapse())
        {
            return PatternAsync(attachment, "native.action.pattern.expand", "Collapsed native element via ExpandCollapsePattern.", action, cancellationToken);
        }

        if (located.Element.TryInvoke())
        {
            return PatternAsync(attachment, "native.action.pattern.invoke", "Clicked native element via InvokePattern; post-action state verification is inconclusive.", action, cancellationToken);
        }

        if (_options.NativeInteraction.UseLegacyAccessibleFallback && located.Element.TryLegacyDefaultAction())
        {
            return PatternAsync(attachment, "native.action.pattern.legacy_default", "Clicked native element via LegacyIAccessible default action; post-action state verification is inconclusive.", action, cancellationToken);
        }

        if (_options.NativeInteraction.EnableNativeInteractionFallback)
        {
            return FallbackClickAsync(attachment, action, located.Element, cancellationToken);
        }

        throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePatternUnsupported, $"Native node '{action.Node.Id}' does not expose a supported click pattern.");
    }

    private Task<NativeActionOutcome> InvokeCoreAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        CancellationToken cancellationToken)
    {
        if (IsExpandAction(action) && located.Element.TryExpand())
        {
            return PatternAsync(attachment, "native.action.pattern.expand", "Invoked native expand via ExpandCollapsePattern.", action, cancellationToken);
        }

        if (IsCollapseAction(action) && located.Element.TryCollapse())
        {
            return PatternAsync(attachment, "native.action.pattern.expand", "Invoked native collapse via ExpandCollapsePattern.", action, cancellationToken);
        }

        if (IsSelectAction(action) && located.Element.TrySelect())
        {
            return VerifiedAsync(attachment, "native.action.pattern.select", "Invoked native selection via SelectionItemPattern.", action, located, VerifySelected, cancellationToken);
        }

        if (located.Element.TryInvoke())
        {
            return PatternAsync(attachment, "native.action.pattern.invoke", "Invoked native element via InvokePattern; post-action state verification is inconclusive.", action, cancellationToken);
        }

        if (_options.NativeInteraction.UseLegacyAccessibleFallback && located.Element.TryLegacyDefaultAction())
        {
            return PatternAsync(attachment, "native.action.pattern.legacy_default", "Invoked native element via LegacyIAccessible default action; post-action state verification is inconclusive.", action, cancellationToken);
        }

        throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePatternUnsupported, $"Native node '{action.Node.Id}' does not expose a supported invoke pattern.");
    }

    private Task<NativeActionOutcome> SetTextCoreAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        CancellationToken cancellationToken)
    {
        var value = action.TextValue ?? string.Empty;

        if (located.Element.TrySetValue(value))
        {
            return VerifiedAsync(attachment, "native.action.pattern.setvalue", "Set native text via ValuePattern.", action, located, (a, l) => string.Equals(l.Element.Value, a.TextValue, StringComparison.Ordinal), cancellationToken);
        }

        if (_options.NativeInteraction.EnableNativeInteractionFallback && _options.NativeInteraction.EnableKeyboardFallback && IsTextInput(action))
        {
            return FallbackSetTextAsync(attachment, action, located.Element, cancellationToken);
        }

        throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePatternUnsupported, $"Native node '{action.Node.Id}' does not expose a writable text pattern.");
    }

    private async Task<NativeActionOutcome> ToggleCoreAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        CancellationToken cancellationToken)
    {
        var desired = action.BoolValue;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (desired is not null && ReadBoolState(located.Element) == desired)
            {
                return new NativeActionOutcome("Native toggle already matched the requested state.");
            }

            if (!located.Element.TryToggle())
            {
                break;
            }

            await RecordActivityAsync(attachment, "native.action.pattern.toggle", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, Metadata(context, attachment, action, "toggle"), cancellationToken).ConfigureAwait(false);

            if (desired is null || ReadBoolState(located.Element) == desired)
            {
                await RecordVerifiedAsync(attachment, action, true, cancellationToken).ConfigureAwait(false);
                return new NativeActionOutcome(desired is null ? "Toggled native element via TogglePattern." : "Toggled native element to the requested state via TogglePattern.");
            }
        }

        throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePostActionVerificationFailed, $"Native node '{action.Node.Id}' could not reach the requested toggle state.");
    }

    private async Task<NativeActionOutcome> SelectCoreAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        CancellationToken cancellationToken)
    {
        if (located.Element.TrySelect())
        {
            await RecordActivityAsync(attachment, "native.action.pattern.select", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, Metadata(context, attachment, action, "select"), cancellationToken).ConfigureAwait(false);
            await RecordVerifiedAsync(attachment, action, VerifySelected(action, located), cancellationToken).ConfigureAwait(false);
            return new NativeActionOutcome("Selected native item via SelectionItemPattern.");
        }

        if (_options.NativeInteraction.ComboAutoExpand)
        {
            located.Element.TryExpand();
        }

        var selectedValue = action.SelectedValue;
        var options = NativeUiAutomationCaptureOptions.FromMetadata(context.Target.Metadata);
        var child = FindSelectableDescendant(located.Element, options, selectedValue, cancellationToken);

        if (child is not null && child.TrySelect())
        {
            located.Element.TryCollapse();
            await RecordActivityAsync(attachment, "native.action.pattern.select", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, Metadata(context, attachment, action, "select"), cancellationToken).ConfigureAwait(false);
            return new NativeActionOutcome($"Selected native item '{selectedValue}' via descendant SelectionItemPattern.");
        }

        throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePatternUnsupported, $"Native node '{action.Node.Id}' could not select item '{selectedValue}'.");
    }

    private async Task<NativeActionOutcome> PatternAsync(
        DesktopSessionAttachment attachment,
        string stage,
        string message,
        ResolvedUiAction action,
        CancellationToken cancellationToken)
    {
        await RecordActivityAsync(attachment, stage, SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = action.Node.Id.Value }, cancellationToken).ConfigureAwait(false);
        return new NativeActionOutcome(message);
    }

    private async Task<NativeActionOutcome> VerifiedAsync(
        DesktopSessionAttachment attachment,
        string stage,
        string message,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        Func<ResolvedUiAction, LocatedNativeUiElement, bool> verify,
        CancellationToken cancellationToken)
    {
        await PatternAsync(attachment, stage, message, action, cancellationToken).ConfigureAwait(false);
        var verified = verify(action, located);
        await RecordVerifiedAsync(attachment, action, verified, cancellationToken).ConfigureAwait(false);

        if (!verified)
        {
            throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativePostActionVerificationFailed, $"Native action on node '{action.Node.Id}' completed, but verification failed.");
        }

        return new NativeActionOutcome(message);
    }

    private async Task<NativeActionOutcome> FallbackClickAsync(
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        INativeUiAutomationElement element,
        CancellationToken cancellationToken)
    {
        RuntimeObservability.NativeActionFallbackTotal.Add(1, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
        await RecordActivityAsync(attachment, "native.action.fallback.keyboard", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = action.Node.Id.Value }, cancellationToken).ConfigureAwait(false);
        var result = await _fallbackExecutor.ClickAsync(element, action, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativeInputFallbackFailed, result.Message);
        }

        return new NativeActionOutcome(result.Message);
    }

    private async Task<NativeActionOutcome> FallbackSetTextAsync(
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        INativeUiAutomationElement element,
        CancellationToken cancellationToken)
    {
        RuntimeObservability.NativeActionFallbackTotal.Add(1, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
        await RecordActivityAsync(attachment, "native.action.fallback.keyboard", SessionObservabilityOutcome.Success, TimeSpan.Zero, null, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = action.Node.Id.Value }, cancellationToken).ConfigureAwait(false);
        var result = await _fallbackExecutor.SetTextAsync(element, action, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativeInputFallbackFailed, result.Message);
        }

        return new NativeActionOutcome(result.Message);
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        Exception? last = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.NativeInteraction.ActionTimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        for (var attempt = 0; attempt <= _options.NativeInteraction.RetryCount; attempt++)
        {
            linked.Token.ThrowIfCancellationRequested();

            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < _options.NativeInteraction.RetryCount && exception is NativeUiAutomationInteractionException or ElementNotAvailableException or InvalidOperationException)
            {
                last = exception;
                if (_options.NativeInteraction.RetryDelayMs > 0)
                {
                    await Task.Delay(_options.NativeInteraction.RetryDelayMs, linked.Token).ConfigureAwait(false);
                }
            }
        }

        throw last ?? new TimeoutException("Native UI action timed out.");
    }

    private Task<NativeActionOutcome> ExecuteWithRetryAsync(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        LocatedNativeUiElement located,
        Func<ResolvedDesktopTargetContext, DesktopSessionAttachment, ResolvedUiAction, LocatedNativeUiElement, CancellationToken, Task<NativeActionOutcome>> execute,
        CancellationToken cancellationToken) =>
        RetryAsync(() => execute(context, attachment, action, located, cancellationToken), cancellationToken);

    private async Task ValidateAttachmentAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var process = _processLocator.GetProcessById(attachment.Process.ProcessId);
        var window = _windowLocator.GetWindowByHandle(attachment.Window.WindowHandle);

        if (process is null || window is null || !window.IsVisible || window.ProcessId != attachment.Process.ProcessId)
        {
            await _attachedSessionStore.RemoveAsync(attachment.SessionId, cancellationToken).ConfigureAwait(false);
            await _recoveryStateStore.MarkAttachmentInvalidAsync(attachment.SessionId, "native.target.lost.during_action", "The attached target was lost during native action execution.", new Dictionary<string, string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            throw new NativeUiAutomationInteractionException(UiCommandFailureCodes.NativeTargetLostDuringAction, "The attached process or window was lost during native action execution.");
        }
    }

    private async Task<UiInteractionResult> FailAsync(
        DesktopSessionAttachment attachment,
        string operation,
        string failureCode,
        string message,
        DateTimeOffset startedAt,
        Exception exception,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var duration = _clock.UtcNow - startedAt;
        RuntimeObservability.NativeActionFailureTotal.Add(1, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
        await _recoveryStateStore.RegisterFailureAsync(attachment.SessionId, RecoveryCategory(failureCode), $"native.action.{operation}.failed", failureCode, message, metadata, cancellationToken).ConfigureAwait(false);
        await RecordActivityAsync(attachment, "native.action.failed", SessionObservabilityOutcome.Failure, duration, failureCode, message, metadata, cancellationToken).ConfigureAwait(false);
        await _observabilityRecorder.RecordAdapterErrorAsync(attachment.SessionId, nameof(WindowsUiAutomationUiInteractionAdapter), $"native.action.{operation}", exception, failureCode, nameof(WindowsUiAutomationUiInteractionAdapter), metadata, cancellationToken).ConfigureAwait(false);
        return UiInteractionResult.Failure(message, failureCode, _clock.UtcNow);
    }

    private static SessionRecoveryFailureCategory RecoveryCategory(string failureCode) =>
        failureCode == UiCommandFailureCodes.NativeTargetLostDuringAction
            ? SessionRecoveryFailureCategory.AttachmentLost
            : failureCode is UiCommandFailureCodes.NativeElementNotFound or UiCommandFailureCodes.NativeElementStale
                ? SessionRecoveryFailureCategory.MetadataDrift
                : SessionRecoveryFailureCategory.CommandExecutionFailure;

    private async Task RecordVerifiedAsync(DesktopSessionAttachment attachment, ResolvedUiAction action, bool verified, CancellationToken cancellationToken)
    {
        var started = _clock.UtcNow;
        var stage = verified ? "native.action.verified" : "native.action.verification_failed";
        var outcome = verified ? SessionObservabilityOutcome.Success : SessionObservabilityOutcome.Failure;
        var code = verified ? null : UiCommandFailureCodes.NativePostActionVerificationFailed;
        var duration = _clock.UtcNow - started;
        RuntimeObservability.NativeActionVerificationDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("session.id", attachment.SessionId.Value));
        await RecordActivityAsync(attachment, stage, outcome, duration, code, null, new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = action.Node.Id.Value }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask RecordActivityAsync(
        DesktopSessionAttachment attachment,
        string stage,
        SessionObservabilityOutcome outcome,
        TimeSpan duration,
        string? reasonCode,
        string? reason,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken) =>
        _observabilityRecorder.RecordActivityAsync(attachment.SessionId, stage, outcome.ToString(), duration, reasonCode, reason, nameof(WindowsUiAutomationUiInteractionAdapter), metadata, cancellationToken);

    private static INativeUiAutomationElement? FindSelectableDescendant(
        INativeUiAutomationElement element,
        NativeUiAutomationCaptureOptions options,
        string? selectedValue,
        CancellationToken cancellationToken)
    {
        foreach (var child in element.GetChildren(options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((string.Equals(child.Name, selectedValue, StringComparison.Ordinal) ||
                 string.Equals(child.Value, selectedValue, StringComparison.Ordinal)) &&
                !child.IsOffscreen &&
                child.IsEnabled)
            {
                return child;
            }

            var descendant = FindSelectableDescendant(child, options, selectedValue, cancellationToken);

            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool VerifySelected(ResolvedUiAction action, LocatedNativeUiElement located) =>
        located.Element.IsSelected == true || string.Equals(located.Element.Name, action.SelectedValue, StringComparison.Ordinal);

    private static bool VerifyToggle(ResolvedUiAction action, LocatedNativeUiElement located) =>
        action.BoolValue is null || ReadBoolState(located.Element) == action.BoolValue;

    private static bool? ReadBoolState(INativeUiAutomationElement element)
    {
        if (element.IsSelected is not null)
        {
            return element.IsSelected;
        }

        return element.Value switch
        {
            "On" or "Checked" or "True" => true,
            "Off" or "Unchecked" or "False" => false,
            _ => null
        };
    }

    private static bool IsToggleLike(ResolvedUiAction action) =>
        Matches(action.Node.Role, "CheckBox") ||
        Matches(action.Node.Role, "ToggleButton") ||
        Matches(GetMetadata(action, "controlType"), "CheckBox");

    private static bool IsTextInput(ResolvedUiAction action) =>
        Matches(action.Node.Role, "TextBox") ||
        Matches(action.Node.Role, "Edit") ||
        Matches(GetMetadata(action, "controlType"), "TextBox") ||
        Matches(GetMetadata(action, "acceptsText"), "true");

    private static bool IsSelectionItem(ResolvedUiAction action) =>
        Matches(action.Node.Role, "ListItem") ||
        Matches(action.Node.Role, "MenuItem") ||
        Matches(GetMetadata(action, "controlType"), "ListItem");

    private static bool IsExpandAction(ResolvedUiAction action) =>
        Matches(action.ActionName, "expand") ||
        Matches(action.ActionName, "open") ||
        Matches(action.ActionName, "show-menu");

    private static bool IsCollapseAction(ResolvedUiAction action) =>
        Matches(action.ActionName, "collapse") ||
        Matches(action.ActionName, "close");

    private static bool IsSelectAction(ResolvedUiAction action) =>
        Matches(action.ActionName, "select");

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? GetMetadata(ResolvedUiAction action, string key) =>
        action.Metadata.TryGetValue(key, out var value) ? value : null;

    private static Dictionary<string, string> Metadata(
        ResolvedDesktopTargetContext context,
        DesktopSessionAttachment attachment,
        ResolvedUiAction action,
        string operation) =>
        new(StringComparer.Ordinal)
        {
            ["profileName"] = context.Profile.ProfileName,
            ["targetKind"] = context.Profile.Kind.ToString(),
            ["processId"] = attachment.Process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["windowHandle"] = attachment.Window.WindowHandle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["nodeId"] = action.Node.Id.Value,
            ["commandKind"] = action.Kind.ToString(),
            ["operation"] = operation
        };

    private sealed record NativeActionOutcome(string Message);
}
