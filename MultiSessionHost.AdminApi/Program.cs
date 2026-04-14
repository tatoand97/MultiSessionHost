using MultiSessionHost.AdminApi;
using MultiSessionHost.AdminApi.Mapping;
using MultiSessionHost.AdminApi.Security;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SessionHostOptions>()
    .Bind(builder.Configuration.GetSection(SessionHostOptions.SectionName))
    .Validate(
        static options => options.TryValidate(out _),
        "The MultiSessionHost configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddSingleton(static serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionHostOptions>>().Value);
builder.Services.AddMultiSessionHostRuntime();
builder.Services.AddSingleton<IAdminAuthorizationPolicy, AllowAllAdminAuthorizationPolicy>();
builder.Services.AddHostedService<AdminApiRuntimeService>();

var app = builder.Build();
var hostOptions = app.Services.GetRequiredService<SessionHostOptions>();
app.Urls.Add(hostOptions.AdminApiUrl);

app.MapGet(
    "/health",
    async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(sessionCoordinator.GetProcessHealth().ToDto());
    });

app.MapGet(
    "/sessions",
    async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapGet(
    "/sessions/{id}",
    async Task<IResult> (string id, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapPost(
    "/sessions/{id}/start",
    async Task<IResult> (string id, StartSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapPost(
    "/sessions/{id}/stop",
    async Task<IResult> (string id, StopSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapPost(
    "/sessions/{id}/pause",
    async Task<IResult> (string id, PauseSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapPost(
    "/sessions/{id}/resume",
    async Task<IResult> (string id, ResumeSessionRequest? request, HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
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

app.MapGet(
    "/metrics",
    async Task<IResult> (HttpContext httpContext, IAdminAuthorizationPolicy authorizationPolicy, ISessionCoordinator sessionCoordinator, CancellationToken cancellationToken) =>
    {
        if (!await authorizationPolicy.IsAuthorizedAsync(httpContext, cancellationToken).ConfigureAwait(false))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(sessionCoordinator.GetProcessHealth().ToDto());
    });

await app.RunAsync().ConfigureAwait(false);

static bool TryParseSessionId(string value, out SessionId sessionId, out string? error)
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
