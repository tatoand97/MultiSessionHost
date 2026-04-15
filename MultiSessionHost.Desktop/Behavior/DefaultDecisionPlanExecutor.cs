using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Behavior;

public sealed class DefaultDecisionPlanExecutor : IDecisionPlanExecutor
{
    private readonly SessionHostOptions _options;
    private readonly ISessionDecisionPlanStore _decisionPlanStore;
    private readonly ISessionDomainStateStore _sessionDomainStateStore;
    private readonly ISessionRiskAssessmentStore _riskAssessmentStore;
    private readonly ISessionActivityStateStore _activityStateStore;
    private readonly ISessionDecisionPlanExecutionStore _executionStore;
    private readonly IReadOnlyList<IDecisionDirectiveHandler> _directiveHandlers;
    private readonly IClock _clock;
    private readonly ILogger<DefaultDecisionPlanExecutor> _logger;

    public DefaultDecisionPlanExecutor(
        SessionHostOptions options,
        ISessionDecisionPlanStore decisionPlanStore,
        ISessionDomainStateStore sessionDomainStateStore,
        ISessionRiskAssessmentStore riskAssessmentStore,
        ISessionActivityStateStore activityStateStore,
        ISessionDecisionPlanExecutionStore executionStore,
        IEnumerable<IDecisionDirectiveHandler> directiveHandlers,
        IClock clock,
        ILogger<DefaultDecisionPlanExecutor> logger)
    {
        _options = options;
        _decisionPlanStore = decisionPlanStore;
        _sessionDomainStateStore = sessionDomainStateStore;
        _riskAssessmentStore = riskAssessmentStore;
        _activityStateStore = activityStateStore;
        _executionStore = executionStore;
        _directiveHandlers = directiveHandlers.ToArray();
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<DecisionPlanExecutionResult> ExecuteLatestAsync(SessionId sessionId, bool wasAutoExecuted, CancellationToken cancellationToken)
    {
        var plan = await _decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Decision plan for session '{sessionId}' was not found.");
        var domainState = await _sessionDomainStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var riskAssessment = await _riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var activitySnapshot = await _activityStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var context = new DecisionPlanExecutionContext(
            sessionId,
            plan,
            domainState,
            riskAssessment,
            activitySnapshot,
            _clock.UtcNow,
            wasAutoExecuted);

        return await ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DecisionPlanExecutionResult> ExecuteAsync(DecisionPlanExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.DecisionExecution;
        if (!options.EnableDecisionExecution)
        {
            var now = _clock.UtcNow;
            return new DecisionPlanExecutionResult(
                context.SessionId,
                ComputePlanFingerprint(context.DecisionPlan),
                now,
                now,
                now,
                DecisionPlanExecutionStatus.Skipped,
                context.WasAutoExecuted,
                [],
                new DecisionPlanExecutionSummary(0, 0, 0, 0, 0, 0, 0, 0, [], [], []),
                DeferredUntilUtc: null,
                FailureReason: null,
                Warnings: ["Decision execution is disabled by configuration."],
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["skipReason"] = "execution-disabled"
                });
        }

        await _executionStore.InitializeIfMissingAsync(context.SessionId, cancellationToken).ConfigureAwait(false);
        var startedAt = _clock.UtcNow;
        var planFingerprint = ComputePlanFingerprint(context.DecisionPlan);
        var previous = await _executionStore.GetCurrentAsync(context.SessionId, cancellationToken).ConfigureAwait(false);

        if (ShouldSuppressDuplicate(planFingerprint, previous, startedAt, out var suppressionReason))
        {
            var suppressedResult = CreateSuppressedExecutionResult(context, startedAt, planFingerprint, suppressionReason!);
            if (options.RecordNoOpExecutions)
            {
                await PersistAsync(context.SessionId, suppressedResult, cancellationToken).ConfigureAwait(false);
            }

            return suppressedResult;
        }

        var directiveResults = new List<DecisionDirectiveExecutionResult>(context.DecisionPlan.Directives.Count);
        var warnings = new List<string>();
        string? failureReason = null;
        DateTimeOffset? deferredUntilUtc = null;
        var stopExecution = false;

        var directiveContext = new DecisionDirectiveExecutionContext(
            context.SessionId,
            context.DecisionPlan,
            context.DomainState,
            context.RiskAssessment,
            context.ActivitySnapshot,
            startedAt,
            context.WasAutoExecuted);

        foreach (var directive in context.DecisionPlan.Directives)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopExecution)
            {
                directiveResults.Add(CreateSkippedDirectiveResult(directive, startedAt, "Skipped because execution flow was already terminated."));
                continue;
            }

            if (TryResolveDirectiveDeferredUntilUtc(directive, startedAt, out var directiveDeferredUntilUtc) &&
                directiveDeferredUntilUtc > startedAt)
            {
                var deferredResult = CreateDeferredDirectiveResult(directive, startedAt, directiveDeferredUntilUtc);
                directiveResults.Add(deferredResult);
                deferredUntilUtc = deferredUntilUtc is null || directiveDeferredUntilUtc > deferredUntilUtc
                    ? directiveDeferredUntilUtc
                    : deferredUntilUtc;
                stopExecution = true;
                continue;
            }

            var handler = _directiveHandlers.FirstOrDefault(candidate => candidate.CanHandle(directive));
            DecisionDirectiveExecutionResult result;

            if (handler is null)
            {
                result = CreateNotHandledDirectiveResult(directive, startedAt);
                warnings.Add($"No directive handler is registered for '{directive.DirectiveKind}'.");

                if (options.FailOnUnhandledBlockingDirective && IsBlockingDirective(directive, result))
                {
                    failureReason = $"Unhandled blocking directive '{directive.DirectiveKind}'.";
                    stopExecution = true;
                }
            }
            else
            {
                result = await handler.ExecuteAsync(directiveContext, directive, cancellationToken).ConfigureAwait(false);
                result = NormalizeDirectiveResult(directive, result, startedAt);

                if (result.Status == DecisionDirectiveExecutionStatus.Failed)
                {
                    failureReason ??= result.Message;
                }

                if (result.DeferredUntilUtc is not null)
                {
                    deferredUntilUtc = deferredUntilUtc is null
                        ? result.DeferredUntilUtc
                        : (result.DeferredUntilUtc > deferredUntilUtc ? result.DeferredUntilUtc : deferredUntilUtc);
                }

                if (result.Status is DecisionDirectiveExecutionStatus.Aborted)
                {
                    stopExecution = true;
                }
                else if (result.Status is DecisionDirectiveExecutionStatus.Blocked or DecisionDirectiveExecutionStatus.Deferred)
                {
                    stopExecution = true;
                }
                else if (IsBlockingDirective(directive, result))
                {
                    stopExecution = true;
                }
            }

            directiveResults.Add(result);
        }

        var completedAt = _clock.UtcNow;
        var summary = BuildSummary(directiveResults);
        var status = ResolvePlanStatus(directiveResults, failureReason);
        var resultMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["directiveCount"] = context.DecisionPlan.Directives.Count.ToString(CultureInfo.InvariantCulture),
            ["evaluatedDirectiveCount"] = directiveResults.Count.ToString(CultureInfo.InvariantCulture)
        };

        var executionResult = new DecisionPlanExecutionResult(
            context.SessionId,
            planFingerprint,
            startedAt,
            startedAt,
            completedAt,
            status,
            context.WasAutoExecuted,
            directiveResults,
            summary,
            deferredUntilUtc,
            failureReason,
            warnings,
            resultMetadata);

        if (status != DecisionPlanExecutionStatus.NoOp || options.RecordNoOpExecutions)
        {
            await PersistAsync(context.SessionId, executionResult, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Decision plan execution completed for session '{SessionId}' with status '{Status}' and fingerprint '{Fingerprint}'.",
            context.SessionId,
            status,
            planFingerprint);

        return executionResult;
    }

    private async ValueTask PersistAsync(SessionId sessionId, DecisionPlanExecutionResult result, CancellationToken cancellationToken)
    {
        await _executionStore.UpsertCurrentAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
        await _executionStore.AppendHistoryAsync(
            sessionId,
            new DecisionPlanExecutionRecord(sessionId, _clock.UtcNow, result),
            cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldSuppressDuplicate(
        string planFingerprint,
        DecisionPlanExecutionResult? previous,
        DateTimeOffset now,
        out string? reason)
    {
        reason = null;
        if (previous is null)
        {
            return false;
        }

        if (!string.Equals(previous.PlanFingerprint, planFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        var suppressionWindow = TimeSpan.FromMilliseconds(_options.DecisionExecution.RepeatSuppressionWindowMs);
        if (suppressionWindow <= TimeSpan.Zero)
        {
            return false;
        }

        var elapsed = now - previous.ExecutedAtUtc;
        if (elapsed > suppressionWindow)
        {
            return false;
        }

        reason = $"Execution suppressed because plan fingerprint '{planFingerprint}' already ran {elapsed.TotalMilliseconds:0}ms ago.";
        return true;
    }

    private static DecisionPlanExecutionResult CreateSuppressedExecutionResult(
        DecisionPlanExecutionContext context,
        DateTimeOffset now,
        string planFingerprint,
        string reason)
    {
        var directiveResults = context.DecisionPlan.Directives
            .Select(
                directive => new DecisionDirectiveExecutionResult(
                    directive.DirectiveId,
                    directive.DirectiveKind,
                    directive.SourcePolicy,
                    directive.Priority,
                    DecisionDirectiveExecutionStatus.Skipped,
                    now,
                    now,
                    reason,
                    FailureCode: null,
                    DeferredUntilUtc: null,
                    directive.Metadata))
            .ToArray();
        var summary = BuildSummary(directiveResults);

        return new DecisionPlanExecutionResult(
            context.SessionId,
            planFingerprint,
            now,
            now,
            now,
            DecisionPlanExecutionStatus.Skipped,
            context.WasAutoExecuted,
            directiveResults,
            summary,
            DeferredUntilUtc: null,
            FailureReason: null,
            Warnings: [reason],
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["skipReason"] = "duplicate-suppression"
            });
    }

    private static DecisionDirectiveExecutionResult CreateNotHandledDirectiveResult(DecisionDirective directive, DateTimeOffset now) =>
        new(
            directive.DirectiveId,
            directive.DirectiveKind,
            directive.SourcePolicy,
            directive.Priority,
            DecisionDirectiveExecutionStatus.NotHandled,
            now,
            now,
            $"No handler registered for directive kind '{directive.DirectiveKind}'.",
            FailureCode: null,
            DeferredUntilUtc: null,
            directive.Metadata);

    private static DecisionDirectiveExecutionResult CreateSkippedDirectiveResult(DecisionDirective directive, DateTimeOffset now, string message) =>
        new(
            directive.DirectiveId,
            directive.DirectiveKind,
            directive.SourcePolicy,
            directive.Priority,
            DecisionDirectiveExecutionStatus.Skipped,
            now,
            now,
            message,
            FailureCode: null,
            DeferredUntilUtc: null,
            directive.Metadata);

    private static DecisionDirectiveExecutionResult CreateDeferredDirectiveResult(
        DecisionDirective directive,
        DateTimeOffset now,
        DateTimeOffset deferredUntilUtc)
    {
        var metadata = new Dictionary<string, string>(directive.Metadata, StringComparer.Ordinal)
        {
            ["deferredUntilUtc"] = deferredUntilUtc.ToString("O")
        };

        return new DecisionDirectiveExecutionResult(
            directive.DirectiveId,
            directive.DirectiveKind,
            directive.SourcePolicy,
            directive.Priority,
            DecisionDirectiveExecutionStatus.Deferred,
            now,
            now,
            $"Directive '{directive.DirectiveKind}' deferred until {deferredUntilUtc:O}.",
            FailureCode: null,
            DeferredUntilUtc: deferredUntilUtc,
            metadata);
    }

    private static DecisionDirectiveExecutionResult NormalizeDirectiveResult(
        DecisionDirective directive,
        DecisionDirectiveExecutionResult result,
        DateTimeOffset fallbackStart)
    {
        var completedAt = result.CompletedAtUtc ?? result.StartedAtUtc;
        return result with
        {
            DirectiveId = string.IsNullOrWhiteSpace(result.DirectiveId) ? directive.DirectiveId : result.DirectiveId,
            DirectiveKind = result.DirectiveKind == DecisionDirectiveKind.None ? directive.DirectiveKind : result.DirectiveKind,
            PolicyName = string.IsNullOrWhiteSpace(result.PolicyName) ? directive.SourcePolicy : result.PolicyName,
            Priority = result.Priority == 0 ? directive.Priority : result.Priority,
            StartedAtUtc = result.StartedAtUtc == default ? fallbackStart : result.StartedAtUtc,
            CompletedAtUtc = completedAt
        };
    }

    private static DecisionPlanExecutionSummary BuildSummary(IReadOnlyList<DecisionDirectiveExecutionResult> results)
    {
        var executedKinds = results
            .Where(static result => result.Status is
                DecisionDirectiveExecutionStatus.Succeeded or
                DecisionDirectiveExecutionStatus.Deferred or
                DecisionDirectiveExecutionStatus.Blocked or
                DecisionDirectiveExecutionStatus.Aborted)
            .Select(static result => result.DirectiveKind.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var skippedKinds = results
            .Where(static result => result.Status == DecisionDirectiveExecutionStatus.Skipped)
            .Select(static result => result.DirectiveKind.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unhandledKinds = results
            .Where(static result => result.Status == DecisionDirectiveExecutionStatus.NotHandled)
            .Select(static result => result.DirectiveKind.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new DecisionPlanExecutionSummary(
            results.Count,
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Succeeded),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Failed),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Skipped),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Deferred),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.NotHandled),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Blocked),
            results.Count(static result => result.Status == DecisionDirectiveExecutionStatus.Aborted),
            executedKinds,
            skippedKinds,
            unhandledKinds);
    }

    private static DecisionPlanExecutionStatus ResolvePlanStatus(
        IReadOnlyList<DecisionDirectiveExecutionResult> results,
        string? failureReason)
    {
        if (results.Count == 0)
        {
            return DecisionPlanExecutionStatus.NoOp;
        }

        if (!string.IsNullOrWhiteSpace(failureReason) || results.Any(static result => result.Status == DecisionDirectiveExecutionStatus.Failed))
        {
            return DecisionPlanExecutionStatus.Failed;
        }

        if (results.Any(static result => result.Status == DecisionDirectiveExecutionStatus.Aborted))
        {
            return DecisionPlanExecutionStatus.Aborted;
        }

        if (results.Any(static result => result.Status == DecisionDirectiveExecutionStatus.Deferred))
        {
            return DecisionPlanExecutionStatus.Deferred;
        }

        if (results.Any(static result => result.Status == DecisionDirectiveExecutionStatus.Blocked))
        {
            return DecisionPlanExecutionStatus.Blocked;
        }

        if (results.All(static result => result.Status is DecisionDirectiveExecutionStatus.Skipped or DecisionDirectiveExecutionStatus.NotHandled))
        {
            return DecisionPlanExecutionStatus.NoOp;
        }

        if (results.Any(static result => result.Status == DecisionDirectiveExecutionStatus.Skipped) &&
            results.All(static result => result.Status is DecisionDirectiveExecutionStatus.Skipped or DecisionDirectiveExecutionStatus.Succeeded))
        {
            return DecisionPlanExecutionStatus.Skipped;
        }

        return DecisionPlanExecutionStatus.Succeeded;
    }

    private static bool IsBlockingDirective(DecisionDirective directive, DecisionDirectiveExecutionResult result)
    {
        if (result.Status is DecisionDirectiveExecutionStatus.Blocked or DecisionDirectiveExecutionStatus.Deferred)
        {
            return true;
        }

        if (directive.DirectiveKind is DecisionDirectiveKind.Wait or DecisionDirectiveKind.PauseActivity or DecisionDirectiveKind.Withdraw or DecisionDirectiveKind.Abort)
        {
            return true;
        }

        return TryGetBoolean(directive.Metadata, "blocks") ||
            TryGetBoolean(directive.Metadata, "isBlocking") ||
            TryGetBoolean(directive.Metadata, "blocking");
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static bool TryResolveDirectiveDeferredUntilUtc(
        DecisionDirective directive,
        DateTimeOffset executionStartedAtUtc,
        out DateTimeOffset deferredUntilUtc)
    {
        deferredUntilUtc = executionStartedAtUtc;
        var hasDeferredMetadata = false;

        if (directive.Metadata.TryGetValue("notBeforeUtc", out var notBeforeValue) &&
            DateTimeOffset.TryParse(notBeforeValue, out var notBeforeUtc))
        {
            hasDeferredMetadata = true;
            if (notBeforeUtc > deferredUntilUtc)
            {
                deferredUntilUtc = notBeforeUtc;
            }
        }

        if (directive.Metadata.TryGetValue("minimumWaitMs", out var minimumWaitValue) &&
            double.TryParse(minimumWaitValue, out var minimumWaitMs) &&
            minimumWaitMs > 0)
        {
            hasDeferredMetadata = true;
            var minimumWaitUntilUtc = executionStartedAtUtc.AddMilliseconds(minimumWaitMs);
            if (minimumWaitUntilUtc > deferredUntilUtc)
            {
                deferredUntilUtc = minimumWaitUntilUtc;
            }
        }

        return hasDeferredMetadata;
    }

    internal static string ComputePlanFingerprint(DecisionPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(plan.SessionId.Value).Append('|');
        builder.Append(plan.PlanStatus).Append('|');

        foreach (var directive in plan.Directives)
        {
            builder.Append(directive.DirectiveId).Append('|');
            builder.Append(directive.DirectiveKind).Append('|');
            builder.Append(directive.Priority.ToString(CultureInfo.InvariantCulture)).Append('|');
            builder.Append(directive.SourcePolicy).Append('|');
            builder.Append(directive.TargetId ?? string.Empty).Append('|');
            builder.Append(directive.TargetLabel ?? string.Empty).Append('|');
            builder.Append(directive.SuggestedPolicy ?? string.Empty).Append('|');

            foreach (var pair in directive.Metadata.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
            }

            builder.Append('|');
        }

        var payload = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }
}
