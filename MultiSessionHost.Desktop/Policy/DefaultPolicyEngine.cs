using Microsoft.Extensions.Logging;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Risk;

namespace MultiSessionHost.Desktop.Policy;

public sealed class DefaultPolicyEngine : IPolicyEngine
{
    private readonly SessionHostOptions _options;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ISessionStateStore _sessionStateStore;
    private readonly IWorkQueue _workQueue;
    private readonly ISessionUiStateStore _sessionUiStateStore;
    private readonly ISessionDomainStateStore _sessionDomainStateStore;
    private readonly ISessionSemanticExtractionStore _semanticExtractionStore;
    private readonly ISessionRiskAssessmentStore _riskAssessmentStore;
    private readonly IEnumerable<IPolicy> _policies;
    private readonly IDecisionPlanAggregator _aggregator;
    private readonly ISessionDecisionPlanStore _decisionPlanStore;
    private readonly IRuntimePersistenceCoordinator _runtimePersistenceCoordinator;
    private readonly IClock _clock;
    private readonly ILogger<DefaultPolicyEngine> _logger;

    public DefaultPolicyEngine(
        SessionHostOptions options,
        ISessionRegistry sessionRegistry,
        ISessionStateStore sessionStateStore,
        IWorkQueue workQueue,
        ISessionUiStateStore sessionUiStateStore,
        ISessionDomainStateStore sessionDomainStateStore,
        ISessionSemanticExtractionStore semanticExtractionStore,
        ISessionRiskAssessmentStore riskAssessmentStore,
        IEnumerable<IPolicy> policies,
        IDecisionPlanAggregator aggregator,
        ISessionDecisionPlanStore decisionPlanStore,
        IRuntimePersistenceCoordinator runtimePersistenceCoordinator,
        IClock clock,
        ILogger<DefaultPolicyEngine> logger)
    {
        _options = options;
        _sessionRegistry = sessionRegistry;
        _sessionStateStore = sessionStateStore;
        _workQueue = workQueue;
        _sessionUiStateStore = sessionUiStateStore;
        _sessionDomainStateStore = sessionDomainStateStore;
        _semanticExtractionStore = semanticExtractionStore;
        _riskAssessmentStore = riskAssessmentStore;
        _policies = policies;
        _aggregator = aggregator;
        _decisionPlanStore = decisionPlanStore;
        _runtimePersistenceCoordinator = runtimePersistenceCoordinator;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<DecisionPlan> EvaluateAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        if (!_options.PolicyEngine.EnablePolicyEngine)
        {
            var disabledPlan = DecisionPlan.Empty(sessionId, now) with
            {
                Warnings = ["Policy engine is disabled."]
            };
            var storedDisabledPlan = await _decisionPlanStore.UpdateAsync(sessionId, disabledPlan, cancellationToken).ConfigureAwait(false);
            await FlushIfEnabledAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return storedDisabledPlan;
        }

        var context = await BuildContextAsync(sessionId, now, cancellationToken).ConfigureAwait(false);
        var policies = OrderPolicies().ToArray();
        var results = new List<PolicyEvaluationResult>(policies.Length);

        foreach (var policy in policies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await policy.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (result.DidAbort && _options.PolicyEngine.BlockOnAbort)
            {
                _logger.LogInformation("Policy '{PolicyName}' aborted evaluation for session '{SessionId}'.", policy.Name, sessionId);
                break;
            }
        }

        var plan = _aggregator.Aggregate(sessionId, now, results);
        var storedPlan = await _decisionPlanStore.UpdateAsync(sessionId, plan, cancellationToken).ConfigureAwait(false);
        await FlushIfEnabledAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return storedPlan;
    }

    private Task FlushIfEnabledAsync(SessionId sessionId, CancellationToken cancellationToken) =>
        _options.RuntimePersistence.AutoFlushAfterStateChanges
            ? _runtimePersistenceCoordinator.FlushSessionAsync(sessionId, cancellationToken)
            : Task.CompletedTask;

    private async ValueTask<PolicyEvaluationContext> BuildContextAsync(
        SessionId sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definition = _sessionRegistry.GetById(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var runtime = await _sessionStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Runtime state for session '{sessionId}' was not initialized.");
        var snapshot = new SessionSnapshot(definition, runtime, _workQueue.GetPendingCount(sessionId));
        var uiState = await _sessionUiStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var domainState = await _sessionDomainStateStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Domain state for session '{sessionId}' was not initialized.");
        var semanticExtraction = await _semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var riskAssessment = await _riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return new PolicyEvaluationContext(
            sessionId,
            snapshot,
            uiState,
            domainState,
            semanticExtraction,
            riskAssessment,
            ResolvedDesktopTargetContext: null,
            DesktopSessionAttachment: null,
            now);
    }

    private IEnumerable<IPolicy> OrderPolicies()
    {
        var policiesByName = _policies.ToDictionary(static policy => policy.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var policyName in _options.PolicyEngine.PolicyOrder)
        {
            if (policiesByName.TryGetValue(policyName, out var policy))
            {
                yield return policy;
            }
        }
    }
}
