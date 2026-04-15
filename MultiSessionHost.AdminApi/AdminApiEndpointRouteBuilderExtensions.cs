using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MultiSessionHost.AdminApi.Mapping;
using MultiSessionHost.AdminApi.Security;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;

namespace MultiSessionHost.AdminApi;

public static class AdminApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapAdminApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

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
}
