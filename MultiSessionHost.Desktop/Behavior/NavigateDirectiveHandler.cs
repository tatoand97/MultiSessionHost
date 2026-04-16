using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class NavigateDirectiveHandler : IDecisionDirectiveHandler
{
    private readonly IUiCommandExecutor _uiCommandExecutor;
    private readonly IClock _clock;

    public NavigateDirectiveHandler(IUiCommandExecutor uiCommandExecutor, IClock clock)
    {
        _uiCommandExecutor = uiCommandExecutor;
        _clock = clock;
    }

    public bool CanHandle(DecisionDirective directive) => directive.DirectiveKind == DecisionDirectiveKind.Navigate;

    public async ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken)
    {
        var startedAt = context.ExecutionStartedAtUtc;
        try
        {
            var command = BuildCommand(context.SessionId, directive);
            var commandResult = await _uiCommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            var completedAt = _clock.UtcNow;
            var metadata = new Dictionary<string, string>(directive.Metadata, StringComparer.Ordinal)
            {
                ["uiCommandKind"] = command.Kind.ToString(),
                ["uiCommandSucceeded"] = commandResult.Succeeded.ToString(),
                ["uiCommandMessage"] = commandResult.Message
            };

            return new DecisionDirectiveExecutionResult(
                directive.DirectiveId,
                directive.DirectiveKind,
                directive.SourcePolicy,
                directive.Priority,
                commandResult.Succeeded ? DecisionDirectiveExecutionStatus.Succeeded : DecisionDirectiveExecutionStatus.Failed,
                startedAt,
                completedAt,
                commandResult.Message,
                commandResult.FailureCode,
                DeferredUntilUtc: null,
                metadata);
        }
        catch (Exception exception)
        {
            var completedAt = _clock.UtcNow;
            return new DecisionDirectiveExecutionResult(
                directive.DirectiveId,
                directive.DirectiveKind,
                directive.SourcePolicy,
                directive.Priority,
                DecisionDirectiveExecutionStatus.Failed,
                startedAt,
                completedAt,
                $"Navigate directive failed: {exception.Message}",
                "behavior.navigate.failed",
                DeferredUntilUtc: null,
                directive.Metadata);
        }
    }

    private static UiCommand BuildCommand(SessionId sessionId, DecisionDirective directive)
    {
        if (!directive.Metadata.TryGetValue("uiCommandKind", out var commandKindValue) || !Enum.TryParse<UiCommandKind>(commandKindValue, ignoreCase: true, out var commandKind))
        {
            throw new InvalidOperationException($"Navigate directive '{directive.DirectiveId}' does not define a valid UI command kind.");
        }

        UiNodeId? nodeId = null;
        if (directive.Metadata.TryGetValue("uiNodeId", out var nodeIdValue) && !string.IsNullOrWhiteSpace(nodeIdValue))
        {
            nodeId = new UiNodeId(nodeIdValue);
        }
        else if (directive.TargetId is not null)
        {
            nodeId = new UiNodeId(directive.TargetId);
        }

        var actionName = directive.Metadata.TryGetValue("uiActionName", out var actionNameValue) && !string.IsNullOrWhiteSpace(actionNameValue)
            ? actionNameValue
            : null;
        var textValue = directive.Metadata.TryGetValue("uiTextValue", out var textValueValue) ? textValueValue : null;
        var selectedValue = directive.Metadata.TryGetValue("uiSelectedValue", out var selectedValueValue) ? selectedValueValue : null;
        bool? boolValue = directive.Metadata.TryGetValue("uiBoolValue", out var boolValueValue) && bool.TryParse(boolValueValue, out var parsedBoolValue)
            ? parsedBoolValue
            : null;

        var metadata = directive.Metadata.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal);

        return commandKind switch
        {
            UiCommandKind.RefreshUi => UiCommand.RefreshUi(sessionId, metadata: metadata),
            UiCommandKind.ClickNode => UiCommand.ClickNode(sessionId, nodeId ?? throw new InvalidOperationException("Navigate directive is missing a node id."), metadata),
            UiCommandKind.InvokeNodeAction => UiCommand.InvokeNodeAction(sessionId, nodeId ?? throw new InvalidOperationException("Navigate directive is missing a node id."), actionName, metadata),
            UiCommandKind.SetText => UiCommand.SetText(sessionId, nodeId ?? throw new InvalidOperationException("Navigate directive is missing a node id."), textValue, metadata),
            UiCommandKind.SelectItem => UiCommand.SelectItem(sessionId, nodeId ?? throw new InvalidOperationException("Navigate directive is missing a node id."), selectedValue, metadata),
            UiCommandKind.ToggleNode => UiCommand.ToggleNode(sessionId, nodeId ?? throw new InvalidOperationException("Navigate directive is missing a node id."), boolValue, metadata),
            _ => throw new InvalidOperationException($"Navigate directive '{directive.DirectiveId}' references unsupported command kind '{commandKind}'.")
        };
    }
}
