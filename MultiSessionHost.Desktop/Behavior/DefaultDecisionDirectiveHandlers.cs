using MultiSessionHost.Core.Models;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Desktop.Policy;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class ObserveDirectiveHandler : IDecisionDirectiveHandler
{
    public bool CanHandle(DecisionDirective directive) => directive.DirectiveKind == DecisionDirectiveKind.Observe;

    public ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken)
    {
        var now = context.ExecutionStartedAtUtc;
        var result = new DecisionDirectiveExecutionResult(
            directive.DirectiveId,
            directive.DirectiveKind,
            directive.SourcePolicy,
            directive.Priority,
            DecisionDirectiveExecutionStatus.Succeeded,
            now,
            now,
            "Observe directive acknowledged without target interaction.",
            FailureCode: null,
            DeferredUntilUtc: null,
            directive.Metadata);
        return ValueTask.FromResult(result);
    }
}

public sealed class WaitDirectiveHandler : IDecisionDirectiveHandler
{
    public bool CanHandle(DecisionDirective directive) => directive.DirectiveKind == DecisionDirectiveKind.Wait;

    public ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken)
    {
        var startedAt = context.ExecutionStartedAtUtc;
        var deferredUntil = ResolveDeferredUntilUtc(directive.Metadata, context.ExecutionStartedAtUtc, startedAt);
        var metadata = new Dictionary<string, string>(directive.Metadata, StringComparer.Ordinal)
        {
            ["deferredUntilUtc"] = deferredUntil.ToString("O")
        };

        var result = new DecisionDirectiveExecutionResult(
            directive.DirectiveId,
            directive.DirectiveKind,
            directive.SourcePolicy,
            directive.Priority,
            DecisionDirectiveExecutionStatus.Deferred,
            startedAt,
            startedAt,
            "Wait directive deferred execution without blocking a worker thread.",
            FailureCode: null,
            DeferredUntilUtc: deferredUntil,
            metadata);

        return ValueTask.FromResult(result);
    }

    private static DateTimeOffset ResolveDeferredUntilUtc(
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset executionStartedAtUtc,
        DateTimeOffset fallbackNow)
    {
        DateTimeOffset? notBefore = null;
        if (metadata.TryGetValue("notBeforeUtc", out var notBeforeValue) &&
            DateTimeOffset.TryParse(notBeforeValue, out var parsedNotBefore))
        {
            notBefore = parsedNotBefore;
        }

        DateTimeOffset? minimumWait = null;
        if (metadata.TryGetValue("minimumWaitMs", out var minimumWaitValue) &&
            double.TryParse(minimumWaitValue, out var parsedMs) &&
            parsedMs > 0)
        {
            minimumWait = executionStartedAtUtc.AddMilliseconds(parsedMs);
        }

        var deferredUntil = fallbackNow;
        if (notBefore is not null && notBefore > deferredUntil)
        {
            deferredUntil = notBefore.Value;
        }

        if (minimumWait is not null && minimumWait > deferredUntil)
        {
            deferredUntil = minimumWait.Value;
        }

        return deferredUntil;
    }
}

public sealed class PauseActivityDirectiveHandler : IDecisionDirectiveHandler
{
    private readonly ISessionControlGateway _sessionControlGateway;
    private readonly IClock _clock;

    public PauseActivityDirectiveHandler(ISessionControlGateway sessionControlGateway, IClock clock)
    {
        _sessionControlGateway = sessionControlGateway;
        _clock = clock;
    }

    public bool CanHandle(DecisionDirective directive) => directive.DirectiveKind == DecisionDirectiveKind.PauseActivity;

    public async ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken)
    {
        var startedAt = context.ExecutionStartedAtUtc;
        try
        {
            await _sessionControlGateway.PauseSessionAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
            var completedAt = _clock.UtcNow;

            return new DecisionDirectiveExecutionResult(
                directive.DirectiveId,
                directive.DirectiveKind,
                directive.SourcePolicy,
                directive.Priority,
                DecisionDirectiveExecutionStatus.Succeeded,
                startedAt,
                completedAt,
                "PauseActivity directive requested a runtime pause.",
                FailureCode: null,
                DeferredUntilUtc: null,
                directive.Metadata);
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
                $"PauseActivity directive failed: {exception.Message}",
                "session-pause-failed",
                DeferredUntilUtc: null,
                directive.Metadata);
        }
    }
}

public sealed class AbortDirectiveHandler : IDecisionDirectiveHandler
{
    private readonly ISessionControlGateway _sessionControlGateway;
    private readonly IClock _clock;

    public AbortDirectiveHandler(ISessionControlGateway sessionControlGateway, IClock clock)
    {
        _sessionControlGateway = sessionControlGateway;
        _clock = clock;
    }

    public bool CanHandle(DecisionDirective directive) => directive.DirectiveKind == DecisionDirectiveKind.Abort;

    public async ValueTask<DecisionDirectiveExecutionResult> ExecuteAsync(
        DecisionDirectiveExecutionContext context,
        DecisionDirective directive,
        CancellationToken cancellationToken)
    {
        var startedAt = context.ExecutionStartedAtUtc;
        try
        {
            await _sessionControlGateway.AbortSessionAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
            var completedAt = _clock.UtcNow;

            return new DecisionDirectiveExecutionResult(
                directive.DirectiveId,
                directive.DirectiveKind,
                directive.SourcePolicy,
                directive.Priority,
                DecisionDirectiveExecutionStatus.Aborted,
                startedAt,
                completedAt,
                "Abort directive requested a runtime stop.",
                FailureCode: null,
                DeferredUntilUtc: null,
                directive.Metadata);
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
                $"Abort directive failed: {exception.Message}",
                "session-abort-failed",
                DeferredUntilUtc: null,
                directive.Metadata);
        }
    }
}
