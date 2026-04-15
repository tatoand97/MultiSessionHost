using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MultiSessionHost.AdminApi.Mapping;
using MultiSessionHost.AdminApi.Security;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.AdminApi;

public static class AdminApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAdminApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            "/bindings",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionTargetBindingManager bindingManager,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var snapshot = await bindingManager.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(snapshot.ToDto());
            });

        endpoints.MapGet(
            "/bindings/{sessionId}",
            async Task<IResult> (
                string sessionId,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionTargetBindingManager bindingManager,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                var binding = await bindingManager.GetAsync(parsedSessionId, cancellationToken).ConfigureAwait(false);
                return binding is null ? Results.NotFound() : Results.Ok(binding.ToDto());
            });

        endpoints.MapPut(
            "/bindings/{sessionId}",
            async Task<IResult> (
                string sessionId,
                SessionTargetBindingUpsertRequest request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionTargetBindingManager bindingManager,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateBinding(parsedSessionId, request, out var binding, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                try
                {
                    var upserted = await bindingManager.UpsertAsync(binding!, cancellationToken).ConfigureAwait(false);
                    return Results.Ok(upserted.ToDto());
                }
                catch (InvalidOperationException exception)
                {
                    return Results.BadRequest(new { Error = exception.Message });
                }
            });

        endpoints.MapDelete(
            "/bindings/{sessionId}",
            async Task<IResult> (
                string sessionId,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionTargetBindingManager bindingManager,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(sessionId, out var parsedSessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await bindingManager.DeleteAsync(parsedSessionId, cancellationToken).ConfigureAwait(false)
                    ? Results.NoContent()
                    : Results.NotFound();
            });

        endpoints.MapGet(
            "/health",
            async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(sessionCoordinator.GetProcessHealth().ToDto());
            });

        endpoints.MapGet(
            "/coordination",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IExecutionCoordinator executionCoordinator,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var snapshot = await executionCoordinator.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(snapshot.ToDto());
            });

        endpoints.MapGet(
            "/coordination/sessions/{id}",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                IExecutionCoordinator executionCoordinator,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var snapshot = await executionCoordinator.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(snapshot.ToDto(sessionId));
            });

        endpoints.MapGet(
            "/sessions",
            async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var processHealth = sessionCoordinator.GetProcessHealth();
                var metricsBySessionId = processHealth.Sessions.ToDictionary(static session => session.SessionId);

                return Results.Ok(
                    sessionCoordinator
                        .GetSessions()
                        .Select(snapshot => snapshot.ToDto(metricsBySessionId[snapshot.SessionId]))
                        .ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}",
            async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                var session = sessionCoordinator.GetSession(sessionId);

                if (session is null)
                {
                    return Results.NotFound();
                }

                var processHealth = sessionCoordinator.GetProcessHealth();
                var metrics = processHealth.Sessions.First(health => health.SessionId == sessionId);
                return Results.Ok(session.ToDto(metrics));
            });

        endpoints.MapGet(
            "/sessions/{id}/ui",
            async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok((sessionCoordinator.GetSessionUiState(sessionId) ?? SessionUiState.Create(sessionId)).ToUiDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/ui/raw",
            async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok((sessionCoordinator.GetSessionUiState(sessionId) ?? SessionUiState.Create(sessionId)).ToUiRawDto());
            });

        endpoints.MapGet(
            "/domain",
            async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(sessionCoordinator.GetSessionDomainStates().Select(static state => state.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/domain",
            async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var state = sessionCoordinator.GetSessionDomainState(sessionId);
                return state is null ? Results.NotFound() : Results.Ok(state.ToDto());
            });

        endpoints.MapGet(
            "/decision-plans",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionDecisionPlanStore decisionPlanStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var plans = await decisionPlanStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(plans.Select(static plan => plan.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().ToDto());
            });

        endpoints.MapGet(
            "/policy-rules/site-selection",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().SiteSelectionRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules/threat-response",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().ThreatResponseRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules/target-prioritization",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().TargetPriorityRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules/resource-usage",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().ResourceUsageRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules/transit",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().TransitRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/policy-rules/abort",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IPolicyRuleProvider policyRuleProvider,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(policyRuleProvider.GetRules().AbortRules.Select(static rule => rule.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/decision-plan",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionDecisionPlanStore decisionPlanStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var plan = await decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return plan is null ? Results.NotFound() : Results.Ok(plan.ToDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/decision-plan/summary",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionDecisionPlanStore decisionPlanStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var plan = await decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return plan is null ? Results.NotFound() : Results.Ok(plan.ToSummaryDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/decision-plan/directives",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionDecisionPlanStore decisionPlanStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var plan = await decisionPlanStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return plan is null
                    ? Results.NotFound()
                    : Results.Ok(plan.Directives.Select(static directive => directive.ToDto()).ToArray());
            });

        endpoints.MapPost(
            "/sessions/{id}/decision-plan/evaluate",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                IPolicyEngine policyEngine,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var plan = await policyEngine.EvaluateAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(plan.ToDto());
            });

        endpoints.MapGet(
            "/semantic",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionSemanticExtractionStore semanticExtractionStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var results = await semanticExtractionStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(results.Select(static result => result.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/risk",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionRiskAssessmentStore riskAssessmentStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var results = await riskAssessmentStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
                return Results.Ok(results.Select(static result => result.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/risk",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionRiskAssessmentStore riskAssessmentStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.ToDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/risk/summary",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionRiskAssessmentStore riskAssessmentStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.Summary.ToDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/risk/entities",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionRiskAssessmentStore riskAssessmentStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.Entities.Select(static entity => entity.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/risk/threats",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionRiskAssessmentStore riskAssessmentStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await riskAssessmentStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null
                    ? Results.NotFound()
                    : Results.Ok(result.Entities.Where(static entity => entity.Disposition == RiskDisposition.Threat).Select(static entity => entity.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/semantic",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionSemanticExtractionStore semanticExtractionStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.ToDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/semantic/summary",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionSemanticExtractionStore semanticExtractionStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.ToSummaryDto());
            });

        endpoints.MapGet(
            "/sessions/{id}/semantic/lists",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionSemanticExtractionStore semanticExtractionStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.Lists.Select(static item => item.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/semantic/alerts",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                ISessionSemanticExtractionStore semanticExtractionStore,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var result = await semanticExtractionStore.GetLatestAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Ok(result.Alerts.Select(static item => item.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/sessions/{id}/target",
            async Task<IResult> (
                string id,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                ISessionCoordinator sessionCoordinator,
                IDesktopTargetProfileResolver targetProfileResolver,
                IAttachedSessionStore attachedSessionStore,
                IDesktopTargetAdapterRegistry adapterRegistry,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                var session = sessionCoordinator.GetSession(sessionId);

                if (session is null)
                {
                    return Results.NotFound();
                }

                try
                {
                    var context = targetProfileResolver.Resolve(session);
                    var attachment = await attachedSessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    var adapter = adapterRegistry.Resolve(context.Profile.Kind);
                    return Results.Ok(context.ToDto(attachment, adapter));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { Error = exception.Message });
                }
            });

        endpoints.MapPost(
            "/sessions/{id}/ui/refresh",
            async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (sessionCoordinator.GetSession(sessionId) is null)
                {
                    return Results.NotFound();
                }

                var state = await sessionCoordinator.RefreshSessionUiAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(state.ToUiRefreshDto());
            });

        endpoints.MapPost(
            "/sessions/{id}/commands",
            async Task<IResult> (
                string id,
                UiCommandRequest request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateCommand(sessionId, request, routeNodeId: null, out var command, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(commandExecutor, command!, cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            "/sessions/{id}/nodes/{nodeId}/click",
            async Task<IResult> (
                string id,
                string nodeId,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateNodeId(nodeId, out var parsedNodeId, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(
                    commandExecutor,
                    UiCommand.ClickNode(sessionId, parsedNodeId!.Value),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            "/sessions/{id}/nodes/{nodeId}/invoke",
            async Task<IResult> (
                string id,
                string nodeId,
                NodeInvokeCommandRequest? request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateNodeId(nodeId, out var parsedNodeId, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(
                    commandExecutor,
                    UiCommand.InvokeNodeAction(sessionId, parsedNodeId!.Value, request?.ActionName, request?.Metadata),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            "/sessions/{id}/nodes/{nodeId}/text",
            async Task<IResult> (
                string id,
                string nodeId,
                NodeTextCommandRequest? request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateNodeId(nodeId, out var parsedNodeId, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(
                    commandExecutor,
                    UiCommand.SetText(sessionId, parsedNodeId!.Value, request?.TextValue, request?.Metadata),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            "/sessions/{id}/nodes/{nodeId}/toggle",
            async Task<IResult> (
                string id,
                string nodeId,
                NodeToggleCommandRequest? request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateNodeId(nodeId, out var parsedNodeId, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(
                    commandExecutor,
                    UiCommand.ToggleNode(sessionId, parsedNodeId!.Value, request?.BoolValue, request?.Metadata),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapPost(
            "/sessions/{id}/nodes/{nodeId}/select",
            async Task<IResult> (
                string id,
                string nodeId,
                NodeSelectCommandRequest? request,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IUiCommandExecutor commandExecutor,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                if (!TryCreateNodeId(nodeId, out var parsedNodeId, out error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                return await ExecuteCommandAsync(
                    commandExecutor,
                    UiCommand.SelectItem(sessionId, parsedNodeId!.Value, request?.SelectedValue, request?.Metadata),
                    cancellationToken).ConfigureAwait(false);
            });

        endpoints.MapGet(
            "/targets",
            async Task<IResult> (
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IDesktopTargetProfileResolver targetProfileResolver,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(targetProfileResolver.GetProfiles().Select(static profile => profile.ToDto()).ToArray());
            });

        endpoints.MapGet(
            "/targets/{profileName}",
            async Task<IResult> (
                string profileName,
                HttpContext httpContext,
                IAdminAuthorizationPolicy authorizationPolicy,
                IDesktopTargetProfileResolver targetProfileResolver,
                CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                var profile = targetProfileResolver.TryGetProfile(profileName);
                return profile is null ? Results.NotFound() : Results.Ok(profile.ToDto());
            });

        endpoints.MapPost(
            "/sessions/{id}/start",
            async Task<IResult> (string id, StartSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                await sessionCoordinator.StartSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Accepted($"/sessions/{sessionId}", new { sessionId = sessionId.Value, request?.Reason });
            });

        endpoints.MapPost(
            "/sessions/{id}/stop",
            async Task<IResult> (string id, StopSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                await sessionCoordinator.StopSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Accepted($"/sessions/{sessionId}", new { sessionId = sessionId.Value, request?.Reason });
            });

        endpoints.MapPost(
            "/sessions/{id}/pause",
            async Task<IResult> (string id, PauseSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                await sessionCoordinator.PauseSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Accepted($"/sessions/{sessionId}", new { sessionId = sessionId.Value, request?.Reason });
            });

        endpoints.MapPost(
            "/sessions/{id}/resume",
            async Task<IResult> (string id, ResumeSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                if (!TryParseSessionId(id, out var sessionId, out var error))
                {
                    return Results.BadRequest(new { Error = error });
                }

                await sessionCoordinator.ResumeSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
                return Results.Accepted($"/sessions/{sessionId}", new { sessionId = sessionId.Value, request?.Reason });
            });

        endpoints.MapGet(
            "/metrics",
            async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
            {
                if (!await IsAuthorizedAsync(httpContext, authorizationPolicy, cancellationToken).ConfigureAwait(false))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(sessionCoordinator.GetProcessHealth().ToDto());
            });

        return endpoints;
    }

    private static Task<bool> IsAuthorizedAsync(HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, CancellationToken cancellationToken) =>
        authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken);

    private static async Task<IResult> ExecuteCommandAsync(
        IUiCommandExecutor commandExecutor,
        UiCommand command,
        CancellationToken cancellationToken)
    {
        var result = await commandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var dto = result.ToDto();
        return result.Succeeded ? Results.Ok(dto) : Results.Conflict(dto);
    }

    private static bool TryParseSessionId(string value, out SessionId sessionId, out string? error)
    {
        try
        {
            sessionId = SessionId.Parse(value);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            sessionId = default;
            error = exception.Message;
            return false;
        }
    }

    private static bool TryCreateCommand(
        SessionId sessionId,
        UiCommandRequest request,
        UiNodeId? routeNodeId,
        out UiCommand? command,
        out string? error)
    {
        if (!Enum.TryParse<UiCommandKind>(request.Kind, ignoreCase: true, out var kind))
        {
            command = null;
            error = $"Ui command kind '{request.Kind}' is not valid.";
            return false;
        }

        UiNodeId? nodeId;

        if (routeNodeId is not null)
        {
            nodeId = routeNodeId;
        }
        else if (!TryCreateNodeId(request.NodeId, out nodeId, out error, allowNull: kind == UiCommandKind.RefreshUi))
        {
            command = null;
            return false;
        }

        command = new UiCommand(
            sessionId,
            nodeId,
            kind,
            request.ActionName,
            request.TextValue,
            request.BoolValue,
            request.SelectedValue,
            request.Metadata);
        error = null;
        return true;
    }

    private static bool TryCreateBinding(
        SessionId sessionId,
        SessionTargetBindingUpsertRequest request,
        out Desktop.Models.SessionTargetBinding? binding,
        out string? error)
    {
        if (request is null)
        {
            binding = null;
            error = "Request body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TargetProfileName))
        {
            binding = null;
            error = "TargetProfileName is required.";
            return false;
        }

        if (!TryCreateOverride(request.Overrides, out var profileOverride, out error))
        {
            binding = null;
            return false;
        }

        binding = new Desktop.Models.SessionTargetBinding(
            sessionId,
            request.TargetProfileName.Trim(),
            (request.Variables ?? new Dictionary<string, string>())
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            profileOverride);
        error = null;
        return true;
    }

    private static bool TryCreateOverride(
        DesktopTargetProfileOverrideDto? overrideDto,
        out Desktop.Models.DesktopTargetProfileOverride? profileOverride,
        out string? error)
    {
        if (overrideDto is null)
        {
            profileOverride = null;
            error = null;
            return true;
        }

        DesktopSessionMatchingMode? matchingMode = null;
        DesktopSessionMatchingMode parsedMatchingMode = default;

        if (!string.IsNullOrWhiteSpace(overrideDto.MatchingMode) &&
            !Enum.TryParse<DesktopSessionMatchingMode>(overrideDto.MatchingMode, ignoreCase: true, out parsedMatchingMode))
        {
            profileOverride = null;
            error = $"MatchingMode '{overrideDto.MatchingMode}' is not valid.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(overrideDto.MatchingMode))
        {
            matchingMode = parsedMatchingMode;
        }

        profileOverride = new Desktop.Models.DesktopTargetProfileOverride(
            overrideDto.ProcessName,
            overrideDto.WindowTitleFragment,
            overrideDto.CommandLineFragmentTemplate,
            overrideDto.BaseAddressTemplate,
            matchingMode,
            overrideDto.Metadata ?? new Dictionary<string, string?>(),
            overrideDto.SupportsUiSnapshots,
            overrideDto.SupportsStateEndpoint);
        error = null;
        return true;
    }

    private static bool TryCreateNodeId(
        string? value,
        out UiNodeId? nodeId,
        out string? error,
        bool allowNull = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowNull)
            {
                nodeId = null;
                error = null;
                return true;
            }

            nodeId = null;
            error = "nodeId is required.";
            return false;
        }

        try
        {
            nodeId = UiNodeId.Parse(value);
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            nodeId = null;
            error = exception.Message;
            return false;
        }
    }
}
